using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using SkiaSharp;

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
if (args.Length == 1 && args[0] == "--dialog-async-smoke")
{
    DialogAsyncSmoke.Run();
    return;
}
if (args.Length == 2 && args[0] == "--probe-head")
{
    using var client = new HttpClient(new HeadProbeHandler()) { Timeout = TimeSpan.FromSeconds(20) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.2 (Minecraft desktop launcher)");
    var head = await new PlayerSkinService(client).GetHeadAsync(args[1]);
    Console.WriteLine(head is null ? "Head unavailable" : $"Head decoded: {head.Image.PixelWidth}x{head.Image.PixelHeight}; frozen={head.Image.IsFrozen}");
    return;
}
if (args.Length == 2 && args[0] == "--design-smoke")
{
    LauncherDesignSmoke.Run(args[1]);
    return;
}
if (args.Length == 1 && args[0] == "--catalog-pagination")
{
    CatalogPaginationSmoke.Run();
    return;
}
if (args.Length == 1 && args[0] == "--probe-curseforge")
{
    var settings = new LauncherSettings();
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.2");
    var service = new CurseForgeService(client, settings.CurseForgeApiBase, settings.CurseForgeApiKey);
    var images = new ImageCacheService(client);
    foreach (var offset in new[] { 0, 30 })
    {
        var page = await service.SearchAsync(new ModrinthSearchRequest("", "mod", "26.2", "fabric", "", "all", "relevance", offset, 30));
        if (page.Items.Count == 0) throw new Exception("CurseForge returned an empty page");
        foreach (var project in page.Items.Where(project => !string.IsNullOrWhiteSpace(project.IconUrl)).Take(3))
            if (await images.GetAsync(project.IconUrl, 96) is null) throw new Exception("CurseForge icon did not load");
        Console.WriteLine($"PASS live CurseForge: offset {offset}, {page.Items.Count} results of {page.TotalHits}, icons decoded");
    }
    return;
}
if (args.Length == 2 && args[0] == "--probe-icon")
{
    using var probeClient = new HttpClient();
    probeClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.2 (Minecraft desktop launcher)");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var icon = await new ImageCacheService(probeClient).GetAsync(args[1], 96);
    Console.WriteLine($"Icon: {icon?.PixelWidth}x{icon?.PixelHeight}; elapsed: {watch.ElapsedMilliseconds}ms");
    return;
}
if (args.Length == 1 && args[0] == "--probe-icons")
{
    using var probeClient = new HttpClient();
    probeClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.2 (Minecraft desktop launcher)");
    var service = new ModrinthService(probeClient);
    var response = await service.SearchAsync(new ModrinthSearchRequest("", "mod", "26.2", "fabric", "", "all", "downloads", 0, 8));
    var images = new ImageCacheService(probeClient);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var results = await Task.WhenAll(response.Items.Select(async item => (item.Title,
        Icon: await images.GetAsync(item.IconUrl, 96))));
    foreach (var result in results)
        Console.WriteLine($"{result.Title}: {result.Icon?.PixelWidth}x{result.Icon?.PixelHeight}");
    Console.WriteLine($"Elapsed: {watch.ElapsedMilliseconds}ms");
    return;
}
if (args.Length == 2 && args[0] == "--probe-jar")
{
    var metadata = await InstalledModMetadataService.ReadAsync(args[1], CancellationToken.None);
    Console.WriteLine($"Name: {metadata.Name}; Icon: {metadata.Icon?.PixelWidth}x{metadata.Icon?.PixelHeight}");
    return;
}
if (args.Length == 3 && args[0] == "--repair")
{
    var directory = Path.GetFullPath(args[1]);
    var archive = Path.GetFullPath(args[2]);
    var instance = JsonSerializer.Deserialize<GameInstance>(await File.ReadAllTextAsync(Path.Combine(directory, "instance.json")), json)!;
    instance.DirectoryPath = directory;
    var registry = new InstalledContentRegistry();
    var records = registry.Load(instance).Where(r => r.SourceId == "modrinth" && r.ProjectType == "modpack"
        && Path.GetFullPath(Path.Combine(directory, r.RelativePath)).Equals(archive, StringComparison.OrdinalIgnoreCase)).ToList();
    int last = -1;
    var progress = new Progress<double>(v => { var bucket = (int)v / 10; if (bucket > last) { last = bucket; Console.WriteLine($"Install: {v:0}%"); } });
    var result = await new MrpackInstaller().InstallAsync(archive, instance, progress);
    foreach (var record in records)
        await registry.MarkAsync(instance, new ModSearchResult { SourceId = record.SourceId, ProjectId = record.ProjectId, ProjectType = record.ProjectType, Title = record.Title }, CatalogInstallState.Installed, result.DestinationPath);
    if (!MrpackInstaller.IsInstalled(instance, result.DestinationPath)) throw new Exception("Installed-file verification failed");
    Console.WriteLine(result.Message);
    Console.WriteLine($"Mods: {instance.ModCount}; catalog records updated: {records.Count}; verified: true");
    return;
}

var temp = Path.Combine(Path.GetTempPath(), "OrbitMrpackTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
int passed = 0;
int skipped = 0;
byte[] payload = Encoding.UTF8.GetBytes("verified mod bytes");
object Entry(string path, string side = "required", string? hash = null, string url = "https://cdn.modrinth.com/test.jar") => new
{
    path, fileSize = payload.Length, hashes = new { sha1 = Convert.ToHexString(SHA1.HashData(payload)), sha512 = hash ?? Convert.ToHexString(SHA512.HashData(payload)) },
    downloads = new[] { url }, env = new { client = side, server = "unsupported" }
};
string Pack(object[] files, Dictionary<string, string>? extras = null)
{
    var path = Path.Combine(temp, Guid.NewGuid().ToString("N") + ".mrpack");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
        writer.Write(JsonSerializer.Serialize(new { formatVersion = 1, game = "minecraft", name = "Test pack", versionId = "1", dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21.1", ["fabric-loader"] = "0.19.3" }, files }));
    foreach (var (name, contents) in extras ?? [])
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(contents);
    return path;
}
byte[] FabricJar(string id, string version, Dictionary<string, object>? depends = null, Dictionary<string, object>? breaks = null)
{
    using var bytes = new MemoryStream();
    using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
    using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
        writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id, version, depends, breaks }));
    return bytes.ToArray();
}
GameInstance Instance()
{
    var instance = new GameInstance { DirectoryPath = Path.Combine(temp, Guid.NewGuid().ToString("N")), Name = "Test", GameVersion = "26.2", Loader = "Fabric", LoaderVersion = "old", LaunchVersionId = "cached-old", OrbitClientEnabled = true };
    Directory.CreateDirectory(instance.ModsPath);
    File.WriteAllText(Path.Combine(instance.DirectoryPath, "instance.json"), JsonSerializer.Serialize(instance, json));
    File.WriteAllText(Path.Combine(instance.ModsPath, "old.jar"), "old mod");
    return instance;
}
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
async Task Test(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine("PASS " + name); }
    catch (OptionalFixtureUnavailable ex) { skipped++; Console.WriteLine("SKIP " + name + ": " + ex.Message); }
}
async Task MustFail(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected failure"); }
using var http = new HttpClient(new FakeHandler(payload));
var installer = new MrpackInstaller(http);
try
{
    passed += await ReleaseAuditSmoke.RunAsync(temp);
    await Test("instance paths survive Windows code pages without changing display names", () =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var method = typeof(InstanceRepository).GetMethod("SafeDirectoryName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var name in new[] { "Новая сборка", "Бедварс тренировка", "Креатив: сборка?", "Виживання Ґрунт їжак", "日本語 中文 🚀", "Café PVP", "CON.txt", "NUL", "...", new string('Я', 100), "CON." + new string('a', 74) + ".z" })
        {
            var folder = (string)method.Invoke(null, new object[] { name })!;
            Check(folder.Length is > 0 and <= 80 && folder.All(c => c is >= ' ' and < '\u007f'), "non-ASCII or invalid directory length");
            Check(folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !folder.EndsWith('.') && !folder.EndsWith(' '), "invalid Windows directory name");
            Check(!new[] { "CON", "NUL" }.Contains(folder.Split('.')[0], StringComparer.OrdinalIgnoreCase), "reserved Windows device name");
            foreach (var codePage in new[] { 1251, 1252, 932, 65001 })
            {
                var encoding = Encoding.GetEncoding(codePage);
                Check(encoding.GetString(encoding.GetBytes(folder)) == folder, "directory corrupted by Windows code page");
            }
        }
        return Task.CompletedTask;
    });
    await Test("existing Unicode instance migration preserves worlds, mods and name without collisions", () =>
    {
        var root = Path.Combine(temp, "path-migration"); Directory.CreateDirectory(root);
        var original = Path.Combine(root, "Новая сборка 🚀");
        Directory.CreateDirectory(Path.Combine(original, "saves", "world"));
        Directory.CreateDirectory(Path.Combine(original, "mods"));
        File.WriteAllBytes(Path.Combine(original, "saves", "world", "level.dat"), payload);
        File.WriteAllBytes(Path.Combine(original, "mods", "example.jar"), payload);
        const string name = "Новая сборка 🚀";
        var instance = new GameInstance { Name = name, DirectoryPath = original };
        var naming = typeof(InstanceRepository).GetMethod("SafeDirectoryName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var preferred = Path.Combine(root, (string)naming.Invoke(null, new object[] { name })!);
        Directory.CreateDirectory(preferred); File.WriteAllText(Path.Combine(preferred, "keep.txt"), "other instance");
        var migrate = typeof(InstanceRepository).GetMethod("EnsureFriendlyDirectory", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        migrate.Invoke(null, new object[] { instance, root });
        Check(instance.Name == name && instance.DirectoryPath != original && instance.DirectoryPath != preferred, "display name or colliding directory was changed");
        Check(Path.GetFileName(instance.DirectoryPath).All(c => c < 128), "migration left non-ASCII folder");
        Check(File.ReadAllBytes(Path.Combine(instance.DirectoryPath, "saves", "world", "level.dat")).SequenceEqual(payload), "world data changed");
        Check(File.ReadAllBytes(Path.Combine(instance.ModsPath, "example.jar")).SequenceEqual(payload), "mod data changed");
        Check(File.ReadAllText(Path.Combine(preferred, "keep.txt")) == "other instance", "colliding instance overwritten");
        var migrated = instance.DirectoryPath;
        migrate.Invoke(null, new object[] { instance, root });
        Check(instance.DirectoryPath == migrated && !Directory.Exists(original), "migration is not stable");
        return Task.CompletedTask;
    });
    await Test("new required 3D component upgrades existing Orbit installations and preserves disabled optimizers", async () =>
    {
        var instance = Instance(); instance.LoaderVersion = "0.19.3";
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, ".orbit"));
        var receipt = Path.Combine(instance.DirectoryPath, ".orbit", "orbit-client-version.txt");
        File.WriteAllText(receipt, "0.9.1");
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "fabric-api.jar"), FabricJar("fabric-api", "0.159.0"));
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "sodium.jar.disabled"), FabricJar("sodium", "0.9.1"));
        const string name = "skinlayers3d-fabric-1.11.3-mc26.2.jar";
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrbitClient", "libs", name))) root = root.Parent;
        if (root is null) throw new OptionalFixtureUnavailable("Skin Layers 3D author JAR is intentionally excluded from public source; see OrbitClient/libs/README.md.");
        var component = await File.ReadAllBytesAsync(Path.Combine(root!.FullName, "OrbitClient", "libs", name));
        var downloads = 0;
        using var componentHttp = new HttpClient(new RouteHandler(uri =>
        {
            Check(uri.Host == "cdn.modrinth.com" && uri.AbsolutePath.EndsWith(name), "unexpected component download");
            downloads++; return component;
        }));
        var service = new OrbitClientService(componentHttp);
        await Task.WhenAll(service.EnsureInstalledAsync(instance),
            new OrbitClientService(componentHttp).EnsureInstalledAsync(instance));
        Check(downloads == 1 && File.ReadAllBytes(Path.Combine(instance.ModsPath, name)).SequenceEqual(component), "3D component not downloaded and verified");
        Check(File.ReadAllText(receipt) == OrbitClientService.ClientVersion, "client receipt not upgraded");
        Check(File.Exists(Path.Combine(instance.ModsPath, "sodium.jar.disabled")), "optional optimizer was reenabled");
        using (var core = ZipFile.OpenRead(Path.Combine(instance.ModsPath, OrbitClientService.JarName)))
        {
            Check(!core.Entries.Any(e => e.FullName.StartsWith("META-INF/jars/skinlayers3d")), "3D dependency was redistributed in core");
            using var metadata = JsonDocument.Parse(core.GetEntry("fabric.mod.json")!.Open());
            Check(metadata.RootElement.GetProperty("version").GetString() == OrbitClientService.ClientVersion, "embedded client version mismatch");
            Check(metadata.RootElement.GetProperty("depends").TryGetProperty("skinlayers3d", out _), "required 3D dependency missing");
            Check(metadata.RootElement.GetProperty("depends").TryGetProperty("holdmyitems", out _), "embedded HMI dependency missing");
            var hmiEntry = core.Entries.SingleOrDefault(e => e.FullName.StartsWith("META-INF/jars/holdmyitems-") && e.FullName.EndsWith(".jar"));
            Check(hmiEntry is not null, "Hold My Items component not nested in installed client");
            using var hmi = new ZipArchive(hmiEntry!.Open(), ZipArchiveMode.Read);
            using var hmiMetadata = JsonDocument.Parse(hmi.GetEntry("fabric.mod.json")!.Open());
            Check(hmiMetadata.RootElement.GetProperty("id").GetString() == "holdmyitems" && hmiMetadata.RootElement.GetProperty("license").GetString() == "CC0-1.0", "HMI identity/license not preserved");
            Check(hmi.GetEntry("LICENSE_holdmyitems") is not null && hmi.GetEntry("ORBIT-PORT.md") is not null, "HMI notices missing");
            Check(hmi.GetEntry("com/holdmylua/source/OrbitHmiSettings.class") is not null && hmi.GetEntry("com/holdmylua/source/HmiEnabledProperty.class") is not null, "HMI integration API not built");
            Check(hmi.GetEntry("resourcepacks/pack_test/assets/minecraft/holdmyitems/item_pose.lua") is not null && hmi.GetEntry("resourcepacks/pack_test/assets/minecraft/holdmyitems/hand_pose.lua") is not null, "original HMI animation scripts missing");
            Check(hmi.GetEntry("META-INF/jars/luaj-jse-3.0.1.jar") is not null, "Lua runtime missing from embedded HMI");
            Check(!core.Entries.Any(e => e.FullName.Contains("Flair") || e.FullName.EndsWith("/flair.png")), "old Flair assets/classes remain in client");
        }
        await service.EnsureInstalledAsync(instance);
        Check(downloads == 1, "existing component was downloaded again");
        File.Move(Path.Combine(instance.ModsPath, name), Path.Combine(instance.ModsPath, name + ".disabled"));
        await MustFail(() => service.EnsureInstalledAsync(instance));
        Check(downloads == 1, "disabled required component was silently reenabled");
    });
    await Test("3D component hash failure preserves the old Orbit client", async () =>
    {
        var instance = Instance(); instance.LoaderVersion = "0.19.3";
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, ".orbit"));
        var receipt = Path.Combine(instance.DirectoryPath, ".orbit", "orbit-client-version.txt");
        File.WriteAllText(receipt, "0.9.1");
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "fabric-api.jar"), FabricJar("fabric-api", "0.159.0"));
        var original = FabricJar("orbit_client", "0.9.1");
        var core = Path.Combine(instance.ModsPath, OrbitClientService.JarName); File.WriteAllBytes(core, original);
        await MustFail(() => new OrbitClientService(http).EnsureInstalledAsync(instance));
        Check(File.ReadAllBytes(core).SequenceEqual(original) && File.ReadAllText(receipt) == "0.9.1", "failed component download replaced core");
        Check(!Directory.EnumerateFiles(instance.ModsPath, "*.orbit-download").Any(), "temporary component was left behind");
    });
    await Test("complete install, overrides, versions, backups and receipt", async () =>
    {
        var instance = Instance();
        File.WriteAllText(Path.Combine(instance.DirectoryPath, "options.txt"), "old options");
        var result = await installer.InstallAsync(Pack([Entry("mods/download.jar"), Entry("mods/server.jar", "unsupported")], new()
        {
            ["overrides/mods/bundled.jar"] = "bundled", ["overrides/config/test.json"] = "common",
            ["client-overrides/config/test.json"] = "client", ["server-overrides/mods/server-only.jar"] = "server",
            ["overrides/options.txt"] = "pack options", ["overrides/saves/One Block/level.dat"] = "new world", ["overrides/saves/One Block/session.lock"] = ""
        }), instance);
        Check(instance.ModCount == 2 && File.ReadAllBytes(Path.Combine(instance.ModsPath, "download.jar")).SequenceEqual(payload), "mod content");
        Check(File.ReadAllText(Path.Combine(instance.DirectoryPath, "config/test.json")) == "client", "override priority");
        Check(instance.LoaderVersion == "0.19.3" && instance.GameVersion == "1.21.1" && instance.LaunchVersionId == "" && !instance.OrbitClientEnabled, "launch definition");
        Check(!File.Exists(Path.Combine(instance.DirectoryPath, "saves/One Block/session.lock")), "session lock excluded");
        Check(Directory.EnumerateFiles(Path.Combine(instance.DirectoryPath, ".orbit/modpack-backups"), "old.jar", SearchOption.AllDirectories).Count() == 1, "old mods backup");
        Check(Directory.EnumerateFiles(Path.Combine(instance.DirectoryPath, ".orbit/modpack-backups"), "options.txt", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "old options"), "options backup");
        Check(result.InstalledDirectly && MrpackInstaller.IsInstalled(instance, result.DestinationPath), "receipt");
        var registry = new InstalledContentRegistry();
        var project = new ModSearchResult { ProjectType = "modpack", ProjectId = "test" };
        await registry.MarkAsync(instance, project, CatalogInstallState.Installed, result.DestinationPath);
        Check(registry.FileStillExists(instance, registry.Load(instance).Single()), "persisted installed state");
        File.Move(Path.Combine(instance.ModsPath, "download.jar"), Path.Combine(instance.ModsPath, "download.jar.disabled"));
        Check(MrpackInstaller.IsInstalled(instance, result.DestinationPath), "disabled mod remains installed");
        File.Delete(Path.Combine(instance.ModsPath, "download.jar.disabled"));
        Check(!registry.FileStillExists(instance, registry.Load(instance).Single()), "missing mod invalidates receipt");
    });
    await Test("existing worlds remain untouched", async () =>
    {
        var instance = Instance();
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, "saves/One Block"));
        File.WriteAllText(Path.Combine(instance.DirectoryPath, "saves/One Block/level.dat"), "player world");
        await installer.InstallAsync(Pack([], new() { ["overrides/saves/One Block/level.dat"] = "pack world", ["overrides/saves/One Block/extra.dat"] = "extra" }), instance);
        Check(File.ReadAllText(Path.Combine(instance.DirectoryPath, "saves/One Block/level.dat")) == "player world" && !File.Exists(Path.Combine(instance.DirectoryPath, "saves/One Block/extra.dat")), "world overwritten");
    });
    await Test("hash failure leaves original instance intact", async () =>
    {
        var instance = Instance();
        await MustFail(() => installer.InstallAsync(Pack([Entry("mods/bad.jar", hash: new string('0', 128))]), instance));
        Check(File.ReadAllText(Path.Combine(instance.ModsPath, "old.jar")) == "old mod" && instance.GameVersion == "26.2", "changed on failed download");
    });
    foreach (var path in new[] { "../escape.jar", "mods/../../escape.jar", "C:/escape.jar", "mods/a.jar:stream", "mods/CON.jar", ".orbit/inject.json", "overrides.exe" })
        await Test("unsafe path rejected: " + path, async () =>
        {
            var instance = Instance();
            await MustFail(() => installer.InstallAsync(Pack([Entry(path)]), instance));
            Check(File.Exists(Path.Combine(instance.ModsPath, "old.jar")), "unsafe pack changed instance");
        });
    await Test("untrusted download host rejected", () => MustFail(() => installer.InstallAsync(Pack([Entry("mods/test.jar", url: "https://example.com/test.jar")]), Instance())));
    await Test("commit error restores old mods and definition", async () =>
    {
        var instance = Instance();
        // A directory at a file destination makes commit fail after replacing the mod directory.
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, "config/blocked.json"));
        await MustFail(() => installer.InstallAsync(Pack([Entry("mods/new.jar")], new() { ["overrides/config/blocked.json"] = "file" }), instance));
        Check(File.ReadAllText(Path.Combine(instance.ModsPath, "old.jar")) == "old mod" && instance.ModCount == 1 && instance.LaunchVersionId == "cached-old", "rollback failed");
    });
    await Test("locked old mod is never deleted on failed move", async () =>
    {
        var instance = Instance();
        using var locked = new FileStream(Path.Combine(instance.ModsPath, "old.jar"), FileMode.Open, FileAccess.Read, FileShare.None);
        await MustFail(() => installer.InstallAsync(Pack([Entry("mods/new.jar")]), instance));
        Check(File.Exists(Path.Combine(instance.ModsPath, "old.jar")), "original mod lost");
    });
    await Test("cancelled installation does not change instance", async () =>
    {
        var instance = Instance();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await MustFail(() => installer.InstallAsync(Pack([Entry("mods/new.jar")]), instance, token: cancel.Token));
        Check(File.Exists(Path.Combine(instance.ModsPath, "old.jar")), "cancel changed mods");
    });
    await Test("previously downloaded modpack can now be installed", () =>
    {
        var project = new ModSearchResult { ProjectType = "modpack" };
        project.SetInstallState(CatalogInstallState.Downloaded);
        Check(project.CanInstall, "download-only status prevents install");
        project.SetInstallState(CatalogInstallState.Installed); Check(!project.CanInstall, "installed pack offered again");
        return Task.CompletedTask;
    });
    await Test("installed Fabric mod uses its displayed name", async () =>
    {
        var path = Path.Combine(temp, "opaque-file-name.jar");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                writer.Write("{\"id\":\"example\",\"name\":\"Friendly Mod Name\",\"icon\":\"assets/example/icon.png\"}");
            using var bitmap = new SKBitmap(16, 16);
            bitmap.Erase(SKColors.Purple);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var entry = zip.CreateEntry("assets/example/icon.png").Open();
            encoded.SaveTo(entry);
        }
        var metadata = await InstalledModMetadataService.ReadAsync(path, CancellationToken.None);
        Check(metadata.Name == "Friendly Mod Name", "jar metadata title not read");
        Check(metadata.Icon is not null, "embedded jar icon not decoded");
    });
    await Test("catalog decodes Modrinth WebP icons", async () =>
    {
        using var bitmap = new SKBitmap(96, 96);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 90);
        Check(encoded is not null, "WebP test image encoder unavailable");
        using var client = new HttpClient(new FakeHandler(encoded!.ToArray()));
        var loader = new ImageCacheService(client);
        var icon = await loader.GetAsync("https://cdn.modrinth.com/test/icon.webp", 96);
        Check(icon is not null && icon.PixelWidth == 96, "WebP catalog icon not decoded");
        var pixel = new byte[4];
        icon!.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Check(pixel[0] > pixel[2] && pixel[3] == 255, "decoded icon colors or alpha are wrong");
    });
    await Test("catalog cards show only supported Minecraft loaders", async () =>
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            total_hits = 1,
            hits = new[] { new
            {
                project_id = "project", title = "Example", description = "Example mod", author = "Author",
                project_type = "mod", downloads = 12500, follows = 410,
                categories = new[] { "fabric", "forge", "bukkit", "optimization" },
                display_categories = new[] { "bukkit", "optimization" }, client_side = "required", server_side = "optional",
                date_modified = "2026-09-20T00:00:00Z"
            } }
        });
        using var client = new HttpClient(new FakeHandler(payload));
        var result = await new ModrinthService(client, "https://test.invalid").SearchAsync(
            new ModrinthSearchRequest("", "mod", "", "", "", "all", "relevance"));
        var mod = result.Items.Single();
        Check(mod.Loaders.SequenceEqual(["Fabric", "Forge"]), "unsupported loader appeared on the card");
        Check(mod.Categories.SequenceEqual(["Оптимизация"]) && mod.Followers == 410 && mod.Environment == "Клиент и сервер",
            "catalog card metadata was not parsed");
    });
    await Test("account head keeps symmetric eyes and clean transparent overlay", async () =>
    {
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.Transparent);
        for (var y = 8; y < 16; y++)
            for (var x = 8; x < 16; x++)
                bitmap.SetPixel(x, y, SKColors.BurlyWood);
        bitmap.SetPixel(10, 12, SKColors.DarkGreen);
        bitmap.SetPixel(13, 12, SKColors.DarkGreen);
        bitmap.SetPixel(42, 8, SKColors.Brown);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var texture = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { textures = new { SKIN = new { url = "http://test.invalid/skin.png" } } }));
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch
        {
            "/users/profiles/minecraft/player" => JsonSerializer.SerializeToUtf8Bytes(new { id = "player-id" }),
            "/session/minecraft/profile/player-id" => JsonSerializer.SerializeToUtf8Bytes(new { properties = new[] { new { name = "textures", value = texture } } }),
            "/skin.png" when uri.Scheme == "https" => encoded.ToArray(),
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        var head = await new PlayerSkinService(client, Path.Combine(temp, "head-pixels")).GetHeadAsync("player");
        Check(head?.Image.PixelWidth == 8 && head.Image.PixelHeight == 8, "head image is not pixel-aligned");
        var pixels = new byte[8 * 8 * 4];
        head!.Image.CopyPixels(pixels, 8 * 4, 0);
        Check(pixels.Skip((4 * 8 + 2) * 4).Take(4).SequenceEqual(pixels.Skip((4 * 8 + 5) * 4).Take(4)),
            "symmetric eye pixels changed while composing the head");
        Check(pixels[(0 * 8 + 2) * 4 + 3] == 255 && pixels[(0 * 8 + 3) * 4 + 3] == 255,
            "transparent overlay left a gap behind the head");
    });
    await Test("saved account head survives restart and unavailable profile servers", async () =>
    {
        var cache = Path.Combine(temp, "head-offline");
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
            canvas.DrawRect(8, 8, 8, 8, new SKPaint { Color = SKColors.Coral });
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        int requests = 0;
        using var client = new HttpClient(new StatusRouteHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        new PlayerSkinService(client, cache).RememberSkin("Player", png.ToArray());
        var restarted = new PlayerSkinService(client, cache);
        var fresh = await restarted.GetHeadAsync("PLAYER");
        Check(fresh is not null && fresh.Image.IsFrozen && requests == 0, "saved head was not available immediately after restart");
        Check(restarted.GetCachedHead("OtherPlayer") is null, "another account inherited the cached head");
        File.SetLastWriteTimeUtc(Directory.GetFiles(cache, "*.png").Single(), DateTime.UtcNow.AddDays(-1));
        var stale = await new PlayerSkinService(client, cache).GetHeadAsync("player");
        Check(stale is not null && requests == 2, "temporary server failure removed the last working head");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await MustFail(async () => { await restarted.GetHeadAsync("player", cancelled.Token); });
    });
    await Test("new skin replaces the saved account head immediately", async () =>
    {
        var cache = Path.Combine(temp, "head-upload");
        using var client = new HttpClient(new StatusRouteHandler(_ => throw new Exception("Uploaded skin must not need a profile request")));
        var service = new PlayerSkinService(client, cache);
        using var bitmap = new SKBitmap(64, 64);
        foreach (var color in new[] { SKColors.Red, SKColors.Blue })
        {
            bitmap.Erase(SKColors.Transparent);
            for (int y = 8; y < 16; y++)
                for (int x = 8; x < 16; x++) bitmap.SetPixel(x, y, color);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            service.RememberSkin("player", png.ToArray());
        }
        var head = await new PlayerSkinService(client, cache).GetHeadAsync("player");
        var pixels = new byte[256];
        head!.Image.CopyPixels(pixels, 32, 0);
        Check(pixels[0] == 255 && pixels[2] == 0 && pixels[3] == 255, "previous skin remained visible after successful upload");
    });
    await Test("corrupt head cache recovers on the next profile request", async () =>
    {
        var cache = Path.Combine(temp, "head-corrupt");
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var texture = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { textures = new { SKIN = new { url = "https://test.invalid/skin.png" } } }));
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch
        {
            "/users/profiles/minecraft/player" => JsonSerializer.SerializeToUtf8Bytes(new { id = "player-id" }),
            "/session/minecraft/profile/player-id" => JsonSerializer.SerializeToUtf8Bytes(new { properties = new[] { new { name = "textures", value = texture } } }),
            "/skin.png" => png.ToArray(),
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        new PlayerSkinService(client, cache).RememberSkin("player", png.ToArray());
        File.WriteAllText(Directory.GetFiles(cache, "*.png").Single(), "damaged image");
        Check(await new PlayerSkinService(client, cache).GetHeadAsync("player") is not null, "damaged cache prevented downloading a valid head");
        Check(new PlayerSkinService(client, cache).GetCachedHead("player") is not null, "recovered head was not saved");
    });
    await Test("cancelled catalog search does not poison shared icon", async () =>
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var client = new HttpClient(new DelayedHandler(encoded.ToArray()));
        var loader = new ImageCacheService(client);
        using var cancelled = new CancellationTokenSource();
        var first = loader.GetAsync("https://cdn.modrinth.com/test/shared-icon.png", 96, cancelled.Token);
        await Task.Delay(15);
        var second = loader.GetAsync("https://cdn.modrinth.com/test/shared-icon.png", 96);
        cancelled.Cancel();
        await MustFail(async () => { await first; });
        Check(await second is not null, "active request inherited a cancelled cached task");
    });
    await Test("temporary icon failure can recover without caching the blank result", async () =>
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var client = new HttpClient(new FlakyHandler(encoded.ToArray()));
        var loader = new ImageCacheService(client);
        const string url = "https://cdn.modrinth.com/test/retry-icon.png";
        Check(await loader.GetAsync(url, 96) is null, "temporary failure unexpectedly succeeded");
        Check(await loader.GetAsync(url, 96) is not null, "failed icon result was cached forever");
    });
    await Test("downloaded data packs enter a new-world picker", async () =>
    {
        var instance = Instance();
        var downloads = Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "datapacks");
        Directory.CreateDirectory(downloads);
        var pack = Path.Combine(downloads, "test-pack.zip");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open()))
            writer.Write("{\"pack\":{\"pack_format\":107,\"description\":\"Test\"}}");
        var picker = Path.Combine(Path.GetTempPath(), "mcworld-" + Guid.NewGuid().ToString("N"));
        using (var bridge = new DatapackWorldCreationBridge(instance.DirectoryPath))
        {
            Directory.CreateDirectory(picker);
            for (var attempt = 0; attempt < 40 && !File.Exists(Path.Combine(picker, "test-pack.zip")); attempt++)
                await Task.Delay(100);
            Check(File.Exists(Path.Combine(picker, "test-pack.zip")), "new-world picker missing pack");
        }
        Directory.Delete(picker, true);
    });
    await Test("Modrinth installs the selected release, not the latest", async () =>
    {
        var bytes = Encoding.UTF8.GetBytes("chosen Modrinth release");
        var file = new { filename = "chosen.zip", url = "https://test.invalid/download/chosen.zip", primary = true,
            hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant() } };
        var chosen = new { id = "chosen", project_id = "project", version_number = "2.0", version_type = "beta",
            date_published = "2026-09-01T00:00:00Z", game_versions = new[] { "26.2" }, loaders = new[] { "minecraft" }, files = new[] { file } };
        using var client = new HttpClient(new RouteHandler(request => request.AbsolutePath switch
        {
            "/project/project/version" => JsonSerializer.SerializeToUtf8Bytes(new[] { chosen }),
            "/version/chosen" => JsonSerializer.SerializeToUtf8Bytes(chosen),
            "/download/chosen.zip" => bytes,
            _ => throw new Exception("Unexpected route: " + request)
        }));
        var service = new ModrinthService(client, "https://test.invalid");
        var versions = await service.GetProjectVersionsAsync("project", "26.2", "minecraft");
        Check(versions.Single().Id == "chosen" && versions.Single().Channel == "beta", "Modrinth versions parsed incorrectly");
        var instance = Instance();
        var result = await service.DownloadAsync(new ModSearchResult { ProjectId = "project", ProjectType = "resourcepack" },
            instance, "26.2", "minecraft", "none", selectedVersionId: "chosen");
        Check(File.ReadAllBytes(result.DestinationPath).SequenceEqual(bytes), "selected Modrinth file not installed");
    });
    await Test("CurseForge installs the selected file", async () =>
    {
        var bytes = Encoding.UTF8.GetBytes("chosen CurseForge release");
        var file = new { id = 42, fileName = "chosen-cf.zip", displayName = "Chosen CF", releaseType = 3,
            fileDate = "2026-09-02T00:00:00Z", gameVersions = new[] { "26.2" }, isAvailable = true,
            downloadUrl = "https://test.invalid/download/cf.zip" };
        using var client = new HttpClient(new RouteHandler(request => request.AbsolutePath switch
        {
            "/mods/42/files" => JsonSerializer.SerializeToUtf8Bytes(new { data = new[] { file } }),
            "/mods/42/files/42" => JsonSerializer.SerializeToUtf8Bytes(new { data = file }),
            "/download/cf.zip" => bytes,
            _ => throw new Exception("Unexpected route: " + request)
        }));
        var service = new CurseForgeService(client, "https://test.invalid", "test-key");
        var versions = await service.GetProjectVersionsAsync("42", "26.2", "");
        Check(versions.Single().Id == "42" && versions.Single().Channel == "alpha", "CurseForge versions parsed incorrectly");
        var instance = Instance();
        var result = await service.DownloadAsync(new ModSearchResult { ProjectId = "42", SourceId = "curseforge", ProjectType = "resourcepack" },
            instance, "26.2", "", "none", selectedFileId: 42);
        Check(File.ReadAllBytes(result.DestinationPath).SequenceEqual(bytes), "selected CurseForge file not installed");
    });
    await Test("repair replaces incompatible mod and installs required dependency", async () =>
    {
        var instance = Instance();
        var updated = Encoding.UTF8.GetBytes("compatible mod");
        var dependency = Encoding.UTF8.GetBytes("required dependency");
        object FileInfo(string name, byte[] bytes) => new { filename = name, url = "https://cdn.modrinth.com/" + name,
            primary = true, hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(bytes)) } };
        var old = new { project_id = "main", game_versions = new[] { "1.21.1" }, loaders = new[] { "fabric" } };
        var current = new { project_id = "main", game_versions = new[] { "26.2" }, loaders = new[] { "fabric" },
            version_type = "release", files = new[] { FileInfo("updated.jar", updated) },
            dependencies = new[] { new { project_id = "dependency", dependency_type = "required" } } };
        var required = new { project_id = "dependency", game_versions = new[] { "26.2" }, loaders = new[] { "fabric" },
            version_type = "release", files = new[] { FileInfo("dependency.jar", dependency) } };
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch
        {
            var path when path.StartsWith("/version_file/") => JsonSerializer.SerializeToUtf8Bytes(old),
            "/project/main/version" => JsonSerializer.SerializeToUtf8Bytes(new[] { current }),
            "/project/dependency/version" => JsonSerializer.SerializeToUtf8Bytes(new[] { required }),
            "/updated.jar" => updated,
            "/dependency.jar" => dependency,
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Replaced == 1 && result.Installed == 1 && result.NeedsAttention == 0, "repair totals");
        Check(File.ReadAllBytes(Path.Combine(instance.ModsPath, "updated.jar")).SequenceEqual(updated), "replacement missing");
        Check(File.ReadAllBytes(Path.Combine(instance.ModsPath, "dependency.jar")).SequenceEqual(dependency), "dependency missing");
        Check(Directory.EnumerateFiles(Path.Combine(instance.DirectoryPath, ".orbit/repair-backups"), "old.jar", SearchOption.AllDirectories).Any(), "backup missing");
    });
    await Test("repair leaves original mod when downloaded hash fails", async () =>
    {
        var instance = Instance();
        var old = new { project_id = "main", game_versions = new[] { "1.21.1" }, loaders = new[] { "fabric" } };
        var candidate = new { project_id = "main", game_versions = new[] { "26.2" }, loaders = new[] { "fabric" },
            version_type = "release", files = new[] { new { filename = "updated.jar", url = "https://cdn.modrinth.com/updated.jar",
                primary = true, hashes = new { sha512 = new string('0', 128) } } } };
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch
        {
            var path when path.StartsWith("/version_file/") => JsonSerializer.SerializeToUtf8Bytes(old),
            "/project/main/version" => JsonSerializer.SerializeToUtf8Bytes(new[] { candidate }),
            "/updated.jar" => Encoding.UTF8.GetBytes("wrong payload"),
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Replaced == 0 && result.NeedsAttention == 1, "hash failure not reported");
        Check(File.ReadAllText(Path.Combine(instance.ModsPath, "old.jar")) == "old mod", "original mod changed");
    });
    await Test("repair leaves duplicate project files untouched", async () =>
    {
        var instance = Instance();
        File.WriteAllText(Path.Combine(instance.ModsPath, "another.jar"), "duplicate mod");
        var old = new { project_id = "main", game_versions = new[] { "1.21.1" }, loaders = new[] { "fabric" } };
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath.StartsWith("/version_file/")
            ? JsonSerializer.SerializeToUtf8Bytes(old) : throw new Exception("Unexpected route: " + uri)));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Replaced == 0 && result.NeedsAttention == 2, "duplicates not reported");
        Check(File.Exists(Path.Combine(instance.ModsPath, "old.jar")) && File.Exists(Path.Combine(instance.ModsPath, "another.jar")), "duplicate files changed");
    });
    await Test("repair resolves Fabric dependency conflicts with a compatible release", async () =>
    {
        var instance = Instance();
        File.Delete(Path.Combine(instance.ModsPath, "old.jar"));
        var sodiumOld = FabricJar("sodium", "0.8.15", breaks: new() { ["iris"] = "<=1.10.7" });
        var sodiumNew = FabricJar("sodium", "0.8.12");
        var iris = FabricJar("iris", "1.10.7", new() { ["sodium"] = "0.8.x" });
        var voxy = FabricJar("voxy", "0.2.16", new() { ["sodium"] = new[] { "=0.8.12" } });
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "sodium-old.jar"), sodiumOld);
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "iris.jar"), iris);
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "voxy.jar"), voxy);
        var hashes = new Dictionary<string, string>
        {
            [Convert.ToHexString(SHA1.HashData(sodiumOld)).ToLowerInvariant()] = "sodium",
            [Convert.ToHexString(SHA1.HashData(iris)).ToLowerInvariant()] = "iris",
            [Convert.ToHexString(SHA1.HashData(voxy)).ToLowerInvariant()] = "voxy"
        };
        object Current(string id) => new { id = id + "-old", project_id = id, game_versions = new[] { "26.2" }, loaders = new[] { "fabric" } };
        var update = new { id = "sodium-new", project_id = "sodium", game_versions = new[] { "26.2" }, loaders = new[] { "fabric" },
            version_type = "release", files = new[] { new { filename = "sodium-new.jar", primary = true,
                url = "https://cdn.modrinth.com/sodium-new.jar", hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(sodiumNew)) } } } };
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch
        {
            var path when path.StartsWith("/version_file/") => JsonSerializer.SerializeToUtf8Bytes(Current(hashes[path["/version_file/".Length..]])),
            "/project/sodium/version" => JsonSerializer.SerializeToUtf8Bytes(new[] { update }),
            "/project/iris/version" or "/project/voxy/version" => JsonSerializer.SerializeToUtf8Bytes(Array.Empty<object>()),
            "/sodium-new.jar" => sodiumNew,
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Replaced == 1 && result.Disabled == 0 && result.NeedsAttention == 0, "Fabric conflict not resolved");
        Check(File.Exists(Path.Combine(instance.ModsPath, "sodium-new.jar")) && !File.Exists(Path.Combine(instance.ModsPath, "sodium-old.jar")), "Sodium not replaced");
    });
    await Test("repair selects Java required by an embedded Fabric component", async () =>
    {
        var instance = Instance();
        instance.GameVersion = "9.9.9";
        File.Delete(Path.Combine(instance.ModsPath, "old.jar"));
        var nested = FabricJar("math", "1.0.0", new() { ["java"] = ">=25" });
        byte[] outer;
        using (var bytes = new MemoryStream())
        {
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                    writer.Write("{\"schemaVersion\":1,\"id\":\"parent\",\"version\":\"1.0.0\",\"jars\":[{\"file\":\"META-INF/jars/math.jar\"}]}");
                using var entry = zip.CreateEntry("META-INF/jars/math.jar").Open();
                entry.Write(nested);
            }
            outer = bytes.ToArray();
        }
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "parent.jar"), outer);
        var runtime = Path.Combine(temp, "runtime-" + Guid.NewGuid().ToString("N"), "java25");
        Directory.CreateDirectory(Path.Combine(runtime, "bin"));
        File.WriteAllText(Path.Combine(runtime, "release"), "JAVA_VERSION=\"25.0.1\"");
        File.WriteAllBytes(Path.Combine(runtime, "bin", "javaw.exe"), []);
        var current = new { id = "parent-version", project_id = "parent", game_versions = new[] { "9.9.9" }, loaders = new[] { "fabric" } };
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath.StartsWith("/version_file/")
            ? JsonSerializer.SerializeToUtf8Bytes(current) : throw new Exception("Unexpected route: " + uri)));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid", Path.GetDirectoryName(runtime))
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.NeedsAttention == 0 && result.Disabled == 0 && instance.JavaPath.EndsWith("javaw.exe"), "Java 25 not selected");
        Check(File.ReadAllText(Path.Combine(instance.DirectoryPath, "instance.json")).Contains("javaw.exe"), "Java selection not persisted");
    });
    await Test("bundled Fabric modules do not trigger false duplicate repairs", async () =>
    {
        var instance = Instance();
        File.Delete(Path.Combine(instance.ModsPath, "old.jar"));
        var nested = FabricJar("fabric-api-base", "1.0.0");
        var external = FabricJar("fabric-api-base", "2.0.0");
        byte[] parent;
        using (var bytes = new MemoryStream())
        {
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                    writer.Write("{\"schemaVersion\":1,\"id\":\"parent\",\"version\":\"1.0.0\",\"jars\":[{\"file\":\"META-INF/jars/api.jar\"}]}");
                using var entry = zip.CreateEntry("META-INF/jars/api.jar").Open();
                entry.Write(nested);
            }
            parent = bytes.ToArray();
        }
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "parent.jar"), parent);
        File.WriteAllBytes(Path.Combine(instance.ModsPath, "external.jar"), external);
        var hashes = new Dictionary<string, string>
        {
            [Convert.ToHexString(SHA1.HashData(parent)).ToLowerInvariant()] = "parent",
            [Convert.ToHexString(SHA1.HashData(external)).ToLowerInvariant()] = "external"
        };
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath.StartsWith("/version_file/")
            ? JsonSerializer.SerializeToUtf8Bytes(new { project_id = hashes[uri.AbsolutePath["/version_file/".Length..]],
                game_versions = new[] { "26.2" }, loaders = new[] { "fabric" } })
            : throw new Exception("Unexpected route: " + uri)));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Disabled == 0 && result.Replaced == 0 && result.NeedsAttention == 0, "bundled modules treated as conflicting duplicates");
    });
    await Test("repair replaces a CurseForge mod for the selected game version", async () =>
    {
        var instance = Instance();
        var oldPath = Path.Combine(instance.ModsPath, "old.jar");
        var updated = FabricJar("example", "2.0.0");
        var registry = new InstalledContentRegistry();
        await registry.MarkAsync(instance, new ModSearchResult { SourceId = "curseforge", ProjectId = "42", ProjectType = "mod", Title = "Example" },
            CatalogInstallState.Installed, oldPath);
        var file = new { id = 7, fileName = "example-26.2.jar", displayName = "Example 26.2", releaseType = 1,
            fileDate = "2026-09-01T00:00:00Z", gameVersions = new[] { "26.2", "Fabric" }, isAvailable = true,
            downloadUrl = "https://test.invalid/download/example.jar",
            hashes = new[] { new { algo = 1, value = Convert.ToHexString(SHA1.HashData(updated)) } } };
        using var client = new HttpClient(new StatusRouteHandler(uri => uri.AbsolutePath switch
        {
            var path when path.StartsWith("/version_file/") => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/mods/42/files" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { data = new[] { file } })) },
            "/mods/42/files/7" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { data = file })) },
            "/download/example.jar" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(updated) },
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid",
            curseForgeApiBase: "https://test.invalid", curseForgeApiKey: "test-key")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Replaced == 1 && File.Exists(Path.Combine(instance.ModsPath, "example-26.2.jar")), "CurseForge mod not replaced");
        Check(Directory.EnumerateFiles(Path.Combine(instance.DirectoryPath, ".orbit/repair-backups"), "old.jar", SearchOption.AllDirectories).Any(), "CurseForge backup missing");
    });
    await Test("repair never disables a shared dependency and creates new conflicts", async () =>
    {
        var instance = Instance();
        File.Delete(Path.Combine(instance.ModsPath, "old.jar"));
        var files = new Dictionary<string, byte[]>
        {
            ["sodium.jar"] = FabricJar("sodium", "0.8.15", breaks: new() { ["iris"] = "<=1.10.7" }),
            ["iris.jar"] = FabricJar("iris", "1.10.7", new() { ["sodium"] = "0.8.x" }),
            ["voxy.jar"] = FabricJar("voxy", "0.2.16", new() { ["sodium"] = "=0.8.12" })
        };
        var hashes = new Dictionary<string, string>();
        foreach (var (name, bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(instance.ModsPath, name), bytes);
            hashes[Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()] = Path.GetFileNameWithoutExtension(name);
        }
        using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch
        {
            var path when path.StartsWith("/version_file/") => JsonSerializer.SerializeToUtf8Bytes(new
            {
                project_id = hashes[path["/version_file/".Length..]], game_versions = new[] { "26.2" }, loaders = new[] { "fabric" }
            }),
            var path when path.StartsWith("/project/") => JsonSerializer.SerializeToUtf8Bytes(Array.Empty<object>()),
            _ => throw new Exception("Unexpected route: " + uri)
        }));
        var result = await new ModCompatibilityRepairService(client, "https://test.invalid")
            .RepairAsync(instance, new Progress<RepairUpdate>());
        Check(result.Disabled == 2 && result.NeedsAttention == 0, "leaf conflicts not isolated");
        Check(File.Exists(Path.Combine(instance.ModsPath, "sodium.jar")), "shared dependency was disabled");
        Check(File.Exists(Path.Combine(instance.ModsPath, "iris.jar.disabled")) && File.Exists(Path.Combine(instance.ModsPath, "voxy.jar.disabled")), "conflicting leaves not disabled");
    });

    Console.WriteLine($"All {passed} tests passed.");
    if (skipped > 0) Console.WriteLine($"Optional integration tests skipped: {skipped}.");
}
finally
{
    // Only the unique, explicitly created test directory is removed.
    Directory.Delete(temp, true);
}

sealed class HeadProbeHandler : DelegatingHandler
{
    public HeadProbeHandler() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Console.WriteLine($"Skin request {request.RequestUri!.Scheme}://{request.RequestUri.Host}: HTTP {(int)response.StatusCode}");
        return response;
    }
}

sealed class OptionalFixtureUnavailable(string message) : Exception(message);

sealed class FakeHandler(byte[] payload) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload), RequestMessage = request });
}

sealed class RouteHandler(Func<Uri, byte[]> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(route(request.RequestUri!)),
            RequestMessage = request
        });
}

sealed class StatusRouteHandler(Func<Uri, HttpResponseMessage> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = route(request.RequestUri!);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}



sealed class DelayedHandler(byte[] payload) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
            RequestMessage = request
        };
    }
}

sealed class FlakyHandler(byte[] payload) : HttpMessageHandler
{
    private int _attempts;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var failing = Interlocked.Increment(ref _attempts) <= 2;
        return Task.FromResult(new HttpResponseMessage(failing ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(failing ? [] : payload),
            RequestMessage = request
        });
    }
}
