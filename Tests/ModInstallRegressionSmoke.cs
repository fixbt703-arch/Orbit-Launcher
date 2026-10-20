using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class ModInstallRegressionSmoke
{
    public static async Task ProbeVerityAsync(string directory)
    {
        if (Directory.Exists(directory)) throw new IOException("Use a new test directory; existing instances are never changed by this probe.");
        var instance = new GameInstance { Name = "Verity install probe", DirectoryPath = Path.GetFullPath(directory), GameVersion = "1.20.1", Loader = "Forge", LoaderVersion = "47.4.28" };
        using var client = LauncherNetwork.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.4.7 (mod installation verification)");
        var settings = new LauncherSettings();
        var service = new CurseForgeService(client, settings.CurseForgeApiBase, settings.CurseForgeApiKey);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var versions = await service.GetProjectVersionsAsync("1591438", "1.20.1", "Forge", deadline.Token);
        var version = versions.FirstOrDefault(version => version.FileName.Equals("verity-6.0.0-beta.9.jar", StringComparison.OrdinalIgnoreCase))
            ?? throw new Exception("The user's Verity version is not available from the official catalog.");
        Console.WriteLine("Official CurseForge: Verity JE " + version.Name + ", file " + version.Id);
        await service.DownloadAsync(new() { ProjectId = "1591438", ProjectType = "mod" }, instance, "1.20.1", "forge", "required", cancellationToken: deadline.Token, selectedFileId: int.Parse(version.Id));
        var before = Directory.GetFiles(instance.ModsPath, "*.jar").ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        Require(before.Keys.Any(name => name!.Contains("geckolib", StringComparison.OrdinalIgnoreCase)) && before.Keys.Any(name => name!.Contains("config", StringComparison.OrdinalIgnoreCase)), "Verity dependencies were not installed");
        foreach (var id in new[] { "238222", "324717" })
            await service.DownloadAsync(new() { ProjectId = id, ProjectType = "mod" }, instance, "1.20.1", "forge", "required", cancellationToken: deadline.Token);
        foreach (var (file, hash) in before)
        {
            var path = Path.Combine(instance.ModsPath, file!);
            Require(File.Exists(path) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash, "Adding another mod changed " + file);
        }
        Console.WriteLine("PASS live Verity JE + required dependencies + JEI + Jade: " + Directory.GetFiles(instance.ModsPath, "*.jar").Length + " intact JARs; no game was executed.");
        foreach (var file in Directory.GetFiles(instance.ModsPath, "*.jar")) Console.WriteLine("  " + Path.GetFileName(file));
    }
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "mod-install");
        int passed = 0;
        GameInstance Instance(string name, string loader = "Forge") => new() { DirectoryPath = Path.Combine(root, name), Name = name, GameVersion = "1.20.1", Loader = loader };
        async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS mod install: " + name); }
        async Task Install(GameInstance instance, string id, string? name = null, string? metadata = null)
        {
            await CatalogModInstaller.InstallAsync(instance, "curseforge", [new(id, async directory =>
            { var path = Path.Combine(directory, name ?? id + ".jar"); await File.WriteAllBytesAsync(path, Jar(id, instance.Loader, metadata)); return path; })], default);
        }
        foreach (var loader in new[] { "Forge", "NeoForge" })
        {
            await Test(loader + " mods sharing minecraft, loader and geckolib never replace each other", async () =>
            {
                var instance = Instance(loader, loader);
                foreach (var id in new[] { "geckolib", "verity", "configlib", "anothermod" }) await Install(instance, id);
                Require(Directory.GetFiles(instance.ModsPath, "*.jar").Length == 4, "unrelated mods were removed");
                await Install(instance, "verity", "verity-update.jar");
                Require(Directory.GetFiles(instance.ModsPath, "*.jar").Length == 4 && !File.Exists(Path.Combine(instance.ModsPath, "verity.jar")), "update lost a different mod or kept duplicate");
            });
            await Test(loader + " batch permits shared dependencies", async () =>
            {
                var instance = Instance(loader + "-batch", loader);
                var requests = new[] { "verity", "geckolib", "configlib" }.Select(id => new CatalogModInstaller.Request(id, async directory =>
                { var path = Path.Combine(directory, id + ".jar"); await File.WriteAllBytesAsync(path, Jar(id, loader)); return path; })).ToArray();
                await CatalogModInstaller.InstallAsync(instance, "curseforge", requests, default);
                Require(Directory.GetFiles(instance.ModsPath).Length == 3, "batch rejected distinct mods");
            });
            await Test(loader + " reads every [[mods]] declaration, ignoring dependency tables", async () =>
            {
                var instance = Instance(loader + "-multi", loader);
                await Install(instance, "bundle", metadata: "[[mods]] # first mod\nmodId = 'first'\n[[dependencies.first]]\nmodId='shared_library'\n[[mods]]\nmodId='second'\n[[dependencies.second]]\nmodId='shared_library'\n");
                await Install(instance, "shared_library");
                Require(File.Exists(Path.Combine(instance.ModsPath, "bundle.jar")), "dependency ID treated as owned ID");
                await Install(instance, "second");
                Require(!File.Exists(Path.Combine(instance.ModsPath, "bundle.jar")) && File.Exists(Path.Combine(instance.ModsPath, "shared_library.jar")), "second actual owned ID not recognized");
            });
        }
        await Test("CurseForge installs required transitive dependencies in one transaction", async () =>
        {
            var instance = Instance("cf-tree"); using var handler = new CfHandler(); using var client = new HttpClient(handler);
            await new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required");
            Require(Directory.GetFiles(instance.ModsPath, "*.jar").Length == 3, "required dependencies disappeared");
            Require(new InstalledContentRegistry().Load(instance).Count == 3, "dependency receipts missing");
        });
        await Test("Forge manifest installs a mandatory library omitted by CurseForge", async () =>
        {
            var instance = Instance("cf-manifest"); using var handler = new CfHandler { OmittedLibrary = true }; using var client = new HttpClient(handler);
            await new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required");
            Require(Directory.GetFiles(instance.ModsPath).Length == 4 && File.Exists(Path.Combine(instance.ModsPath, "mod667299.jar")), "manifest-only YACL dependency missing");
        });
        await Test("unknown manifest library is searched and accepted only after matching its actual mod ID", async () =>
        {
            var instance = Instance("cf-search-library"); using var handler = new CfHandler { SearchLibrary = true }; using var client = new HttpClient(handler);
            await new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required");
            Require(File.Exists(Path.Combine(instance.ModsPath, "mod5.jar")) && !File.Exists(Path.Combine(instance.ModsPath,"mod4.jar")), "fuzzy search installed an unrelated library");
        });
        await Test("manifest search skips a result without a compatible file and continues to the matching library", async () =>
        {
            var instance = Instance("cf-search-no-files"); using var handler = new CfHandler { SearchLibrary = true, CandidateWithoutFiles = true }; using var client = new HttpClient(handler);
            await new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required");
            Require(File.Exists(Path.Combine(instance.ModsPath, "mod5.jar")), "an unrelated search result blocked the correct library");
        });
        foreach (var wrongVersion in new[] { false, true })
            await Test("manifest library must match " + (wrongVersion ? "the declared version range" : "the actual mod ID"), async () =>
            {
                var instance = Instance("cf-manifest-invalid-" + wrongVersion); await Install(instance, "keep");
                using var handler = new CfHandler { OmittedLibrary = true, LibraryIdMismatch = !wrongVersion, LibraryVersionMismatch = wrongVersion }; using var client = new HttpClient(handler);
                await MustFail(() => new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required"));
                Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).SequenceEqual(["keep.jar"]), "incorrect library was committed");
            });
        await Test("CurseForge failed root download leaves no installed dependencies or changed originals", async () =>
        {
            var instance = Instance("cf-rollback"); Directory.CreateDirectory(instance.ModsPath);
            await File.WriteAllBytesAsync(Path.Combine(instance.ModsPath, "old-dep.jar"), Jar("mod2", "Forge"));
            var original = await File.ReadAllBytesAsync(Path.Combine(instance.ModsPath, "old-dep.jar"));
            using var handler = new CfHandler { FailFile = 1 }; using var client = new HttpClient(handler);
            await MustFail(() => new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required"));
            Require(Directory.GetFiles(instance.ModsPath).Length == 1 && original.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(instance.ModsPath, "old-dep.jar"))), "partial installation changed the instance");
        });
        await Test("CurseForge rejects a manually selected incompatible loader before downloading", async () =>
        {
            var instance = Instance("cf-loader"); using var handler = new CfHandler { WrongLoader = true }; using var client = new HttpClient(handler);
            await MustFail(() => new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required", selectedFileId: 1));
            Require(handler.Downloads == 0, "incompatible selected file was downloaded");
        });
        await Test("CurseForge optional dependency download failure omits its required subtree", async () =>
        {
            var instance = Instance("cf-optional"); using var handler = new CfHandler { Optional = true, FailFile = 2 }; using var client = new HttpClient(handler);
            await new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "all");
            Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).SequenceEqual(["mod1.jar"]), "partial optional subtree retained");
        });
        await Test("CurseForge mandatory dependency hash failure preserves the entire instance", async () =>
        {
            var instance = Instance("cf-dependency-failure"); await Install(instance, "keep");
            using var handler = new CfHandler { FailFile = 3 }; using var client = new HttpClient(handler);
            await MustFail(() => new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required"));
            Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).SequenceEqual(["keep.jar"]), "failed dependency changed installed mods");
        });
        await Test("CurseForge rejects another Minecraft version before downloading", async () =>
        {
            var instance = Instance("cf-game"); using var handler = new CfHandler { WrongGame = true }; using var client = new HttpClient(handler);
            await MustFail(() => new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "required", selectedFileId: 1));
            Require(handler.Downloads == 0, "wrong game version downloaded");
        });
        await Test("dependency mode none is explicit and does not fetch dependencies", async () =>
        {
            var instance = Instance("cf-none"); using var handler = new CfHandler(); using var client = new HttpClient(handler);
            await new CurseForgeService(client, "https://fixture.invalid", "fixture-key").DownloadAsync(new() { ProjectId = "1", ProjectType = "mod" }, instance, "1.20.1", "forge", "none");
            Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).SequenceEqual(["mod1.jar"]), "explicit dependency mode ignored");
        });
        await Test("declared incompatible project is rejected without removing existing mods", async () =>
        {
            var instance = Instance("incompatible"); await Install(instance, "existing");
            await MustFail(() => CatalogModInstaller.InstallAsync(instance, "curseforge", [new("new", async directory =>
            { var path=Path.Combine(directory,"new.jar"); await File.WriteAllBytesAsync(path,Jar("new","Forge")); return path; }, IncompatibleProjects:["existing"])], default));
            Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).SequenceEqual(["existing.jar"]), "conflict changed mods");
        });
        await Test("simultaneous catalog installs retain all unrelated mods", async () =>
        {
            var instance = Instance("concurrent");
            await Task.WhenAll(new[] { "first", "second", "third", "fourth" }.Select(id => Install(instance,id)));
            Require(Directory.GetFiles(instance.ModsPath).Length == 4 && new InstalledContentRegistry().Load(instance).Count == 4, "parallel install lost a mod");
        });
        await Test("updating a library cannot break an installed mod's version range", async () =>
        {
            var instance = Instance("library-range");
            await Install(instance, "consumer", metadata:"[[mods]]\nmodId='consumer'\nversion='1'\n[[dependencies.consumer]]\nmodId='library'\nmandatory=true\nversionRange='[1,2)'\n");
            await Install(instance, "library");
            await MustFail(() => Install(instance, "library", "library-new.jar", "[[mods]]\nmodId='library'\nversion='3.0'\n"));
            Require(File.Exists(Path.Combine(instance.ModsPath,"library.jar")) && !File.Exists(Path.Combine(instance.ModsPath,"library-new.jar")), "incompatible dependency update changed installed mods");
            await Install(instance, "library", "library-compatible.jar", "[[mods]]\nmodId='library'\nversion='1.5'\n");
            Require(File.Exists(Path.Combine(instance.ModsPath,"library-compatible.jar")), "compatible update was rejected");
        });
        await Test("invalid JAR without a catalog hash never replaces a working mod", async () =>
        {
            var instance = Instance("corrupt"); await Install(instance, "keep");
            await MustFail(() => CatalogModInstaller.InstallAsync(instance, "curseforge", [new("keep", async directory =>
            { var path = Path.Combine(directory, "keep.jar"); await File.WriteAllTextAsync(path, "<html>service unavailable</html>"); return path; })], default));
            using var zip = ZipFile.OpenRead(Path.Combine(instance.ModsPath, "keep.jar"));
            Require(zip.Entries.Count > 0, "working archive replaced with invalid bytes");
        });
        return passed;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task MustFail(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected failure"); }
    internal static byte[] Jar(string id, string loader, string? metadata = null)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(loader == "NeoForge" ? "META-INF/neoforge.mods.toml" : "META-INF/mods.toml");
            entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(metadata ?? $"modLoader=\"javafml\"\nloaderVersion=\"[47,)\"\n[[mods]]\nmodId=\"{id}\"\nversion=\"1.0.0\"\n[[dependencies.{id}]]\nmodId=\"minecraft\"\nmandatory=true\nversionRange=\"[1.20.1,1.21)\"\n[[dependencies.{id}]]\nmodId=\"forge\"\nmandatory=true\n[[dependencies.{id}]]\nmodId=\"geckolib\"\nmandatory=false\n");
        }
        return bytes.ToArray();
    }
    private sealed class CfHandler : HttpMessageHandler
    {
        public int FailFile { get; init; }
        public bool WrongLoader { get; init; }
        public bool WrongGame { get; init; }
        public bool Optional { get; init; }
        public bool OmittedLibrary { get; init; }
        public bool SearchLibrary { get; init; }
        public bool CandidateWithoutFiles { get; init; }
        public bool LibraryIdMismatch { get; init; }
        public bool LibraryVersionMismatch { get; init; }
        public int Downloads { get; private set; }
        private byte[] Bytes(int id) => Jar("mod" + id, "Forge", id == 1 && SearchLibrary
            ? "[[mods]]\nmodId='mod1'\nversion='1'\n[[dependencies.mod1]]\nmodId='example_library'\nmandatory=true\nversionRange='[1,2)'\n"
            : id == 5 && SearchLibrary ? "[[mods]]\nmodId='example_library'\nversion='1.5'\n" : id == 1 && OmittedLibrary
            ? "[[mods]]\nmodId=\"mod1\"\nversion=\"1\"\n[[dependencies.\"mod1\"]]\nmodId=\"yet_another_config_lib_v3\"\nmandatory=true\nversionRange=\"[3.6,)\"\nside=\"CLIENT\"\n"
            : id == 667299 ? "[[mods]]\nmodId=\"" + (LibraryIdMismatch ? "unrelated_library" : "yet_another_config_lib_v3") + "\"\nversion=\"" + (LibraryVersionMismatch ? "3.5.0" : "3.6.6") + "\"\n" : null);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes;
            if (path == "/mods/search")
            {
                bytes = JsonSerializer.SerializeToUtf8Bytes(new { data = new[] { new { id=4, name="Unrelated library" }, new { id=5, name="Example library" } } });
            }
            else if (parts[0] == "files")
            {
                Downloads++;
                int id = int.Parse(parts[1]); bytes = id == FailFile ? [0, 1, 2] : Bytes(id);
            }
            else if (parts[0] == "mods" && parts.Length >= 3)
            {
                int id = int.Parse(parts[1]);
                if (id == 4 && CandidateWithoutFiles)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage=request, Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { data=Array.Empty<object>() })) });
                var data = new { id, modId = id, fileName = $"mod{id}.jar", isAvailable = true, releaseType = 1, fileDate = "2026-10-01T00:00:00Z",
                    gameVersions = new[] { WrongGame ? "1.21.1" : "1.20.1", WrongLoader ? "Fabric" : "Forge" }, downloadUrl = "https://fixture.invalid/files/" + id,
                    dependencies = id < 3 ? new[] { new { modId = id + 1, relationType = id == 1 && Optional ? 2 : 3 } } : [],
                    hashes = new[] { new { algo = 1, value = Convert.ToHexString(SHA1.HashData(Bytes(id))) } } };
                bytes = parts.Length == 3 ? JsonSerializer.SerializeToUtf8Bytes(new { data = new[] { data } }) : JsonSerializer.SerializeToUtf8Bytes(new { data });
            }
            else throw new Exception("Unexpected fixture request: " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) });
        }
    }
}
