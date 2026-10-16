using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SkiaSharp;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class ReleaseAuditSmoke
{
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "release-audit"); Directory.CreateDirectory(root);
        var failures = new List<Exception>(); int passed = 0;
        async Task Test(string name, Func<Task> action)
        {
            try { await action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { failures.Add(new Exception(name, e)); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        await Test("parallel catalog receipts retain every installed project", async () =>
        {
            var instance = new GameInstance { DirectoryPath = Path.Combine(root, "catalog") };
            Directory.CreateDirectory(instance.ModsPath);
            await Task.WhenAll(Enumerable.Range(0, 40).Select(i => new InstalledContentRegistry().MarkAsync(instance,
                new ModSearchResult { SourceId = "modrinth", ProjectId = "project-" + i, Title = "Project " + i },
                CatalogInstallState.Installed, Path.Combine(instance.ModsPath, i + ".jar"))));
            Require(new InstalledContentRegistry().Load(instance).Count == 40, "installed project receipts were lost");
        });
        await Test("failed instance export preserves the existing backup", async () =>
        {
            var instance = new GameInstance { DirectoryPath = Path.Combine(root, "export-locked") };
            Directory.CreateDirectory(instance.DirectoryPath);
            var file = Path.Combine(instance.DirectoryPath, "level.dat"); File.WriteAllText(file, "world");
            var zip = Path.Combine(root, "backup.zip"); File.WriteAllText(zip, "previous backup");
            using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            try { await new InstanceRepository().ExportAsync(instance, zip); throw new Exception("locked export should fail"); }
            catch (IOException) { }
            Require(File.ReadAllText(zip) == "previous backup", "previous backup was deleted or overwritten");
        });
        await Test("instance export refuses an archive inside the instance", async () =>
        {
            var instance = new GameInstance { DirectoryPath = Path.Combine(root, "export-inside") };
            Directory.CreateDirectory(instance.DirectoryPath);
            var target = Path.Combine(instance.DirectoryPath, "backup.zip"); File.WriteAllText(target, "keep");
            try { await new InstanceRepository().ExportAsync(instance, target); throw new Exception("internal archive should be refused"); }
            catch (IOException) { }
            catch (InvalidOperationException) { }
            Require(File.ReadAllText(target) == "keep", "internal backup was damaged");
        });
        await Test("parallel settings writes remain valid and damaged settings restore their backup", async () =>
        {
            var path = Path.Combine(root, "settings", "settings.json");
            await Task.WhenAll(Enumerable.Range(0, 40).Select(i => new SettingsService(path).SaveAsync(new LauncherSettings { OfflinePlayerName = "Player" + i })));
            var service = new SettingsService(path);
            await service.SaveAsync(new LauncherSettings { OfflinePlayerName = "Previous" });
            await service.SaveAsync(new LauncherSettings { OfflinePlayerName = "Latest" });
            File.WriteAllText(path, "{broken");
            Require((await service.LoadAsync()).OfflinePlayerName == "Previous", "backup settings were not restored");
            Require(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt-*").Length == 1, "damaged settings were not preserved");
            await service.SaveAsync(new LauncherSettings { OfflinePlayerName = "Recovered" });
            Require(JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path + ".bak"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.OfflinePlayerName == "Previous", "recovery replaced the valid backup with corrupt bytes");
        });
        await Test("parallel instance creation never merges folders or IDs", async () =>
        {
            var directory = Path.Combine(root, "parallel-instances");
            var instances = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => new InstanceRepository(directory, Path.Combine(root, "shared")).CreateAsync(new GameInstance { Name = "Одинаковое имя" })));
            Require(instances.Select(i => i.DirectoryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 16, "instance directories were merged");
            Require(instances.Select(i => i.Id).Distinct().Count() == 16, "instance IDs were duplicated");
            foreach (var instance in instances) Require(AtomicJsonFile.Read<GameInstance>(Path.Combine(instance.DirectoryPath, "instance.json"))!.Id == instance.Id, "instance definition was overwritten");
        });
        await Test("failed duplication leaves no partial instance and never removes the original", async () =>
        {
            var directory = Path.Combine(root, "duplicate-failure");
            var repo = new InstanceRepository(directory, Path.Combine(root, "shared"));
            var original = await repo.CreateAsync(new GameInstance { Name = "Original" });
            var file = Path.Combine(original.ModsPath, "locked.jar"); File.WriteAllText(file, "original mod");
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { await repo.DuplicateAsync(original); throw new Exception("locked duplicate should fail"); }
                catch (IOException) { }
            }
            Require((await repo.GetAllAsync()).Count == 1, "partial copy appeared in the library");
            Require(!Directory.EnumerateDirectories(directory).Any(p => Path.GetFileName(p).StartsWith(".orbit-")), "failed staging directory was left behind");
            Require(File.ReadAllText(file) == "original mod", "original instance was changed");
        });
        await Test("ZIP import keeps data and clears machine-specific Java and cached loader profiles", async () =>
        {
            var directory = Path.Combine(root, "portable-import");
            var repo = new InstanceRepository(directory, Path.Combine(root, "shared"));
            var original = await repo.CreateAsync(new GameInstance { Name = "Русская сборка", Loader = "Fabric", LoaderVersion = "0.19.5", LaunchVersionId = "author-cached-profile", JavaPath = @"C:\Author\Java\javaw.exe" });
            Directory.CreateDirectory(Path.Combine(original.DirectoryPath, "saves", "world"));
            File.WriteAllText(Path.Combine(original.DirectoryPath, "saves", "world", "level.dat"), "world data");
            File.WriteAllText(Path.Combine(original.ModsPath, "example.jar"), "mod data");
            await repo.SaveAsync(original);
            var archive = Path.Combine(root, "portable.zip"); await repo.ExportAsync(original, archive);
            var imported = await repo.ImportAsync(archive);
            Require(imported.Id != original.Id && imported.JavaPath == "" && imported.LaunchVersionId == "", "foreign machine configuration was retained");
            Require(imported.Name.Contains("Русская сборка") && File.ReadAllText(Path.Combine(imported.DirectoryPath, "saves", "world", "level.dat")) == "world data", "world/name changed on import");
            Require(File.ReadAllText(Path.Combine(imported.ModsPath, "example.jar")) == "mod data", "mods changed on import");
            Require(!File.Exists(Path.Combine(imported.DirectoryPath, "instance.json.bak")), "source definition backup was imported with the old ID");
            Require(AtomicJsonFile.Read<GameInstance>(Path.Combine(imported.DirectoryPath, "instance.json"))!.Id == imported.Id, "imported definition was not committed");
        });
        await Test("shared-file migration retains conflicting bytes even when file lengths match", async () =>
        {
            var directory = Path.Combine(root, "migration-conflict"); var shared = Path.Combine(root, "migration-shared");
            var repo = new InstanceRepository(directory, shared); var instance = await repo.CreateAsync(new GameInstance { Name = "Conflict" });
            Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, "versions")); Directory.CreateDirectory(Path.Combine(shared, "versions"));
            var local = Path.Combine(instance.DirectoryPath, "versions", "profile.json"); var common = Path.Combine(shared, "versions", "profile.json");
            File.WriteAllText(local, "local"); File.WriteAllText(common, "other");
            File.WriteAllText(Path.Combine(instance.DirectoryPath, "versions", "equal.json"), "same"); File.WriteAllText(Path.Combine(shared, "versions", "equal.json"), "same");
            Require((await repo.GetAllAsync()).Count == 1, "migration hid the instance");
            Require(File.ReadAllText(local) == "local" && File.ReadAllText(common) == "other", "migration discarded a conflicting file");
            Require(!File.Exists(Path.Combine(instance.DirectoryPath, "versions", "equal.json")), "identical duplicate was not merged");
        });
        await Test("instance deletion refuses folders outside the instance root", async () =>
        {
            var external = Path.Combine(root, "keep-outside"); Directory.CreateDirectory(external); File.WriteAllText(Path.Combine(external, "keep.txt"), "keep");
            var repo = new InstanceRepository(Path.Combine(root, "delete-root"), Path.Combine(root, "shared"));
            try { await repo.DeleteAsync(new GameInstance { DirectoryPath = external }); throw new Exception("external deletion should fail"); }
            catch (IOException) { }
            Require(File.ReadAllText(Path.Combine(external, "keep.txt")) == "keep", "external file removed");
        });
        await Test("bad Modrinth and CurseForge checksums preserve already installed files", async () =>
        {
            var expected = Encoding.UTF8.GetBytes("expected bytes"); var corrupt = Encoding.UTF8.GetBytes("corrupt bytes");
            var instance = new GameInstance { DirectoryPath = Path.Combine(root, "hash-preservation"), GameVersion = "26.2" };
            Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, "resourcepacks"));
            var target = Path.Combine(instance.DirectoryPath, "resourcepacks", "keep.zip"); File.WriteAllText(target, "installed original");
            var version = new { id = "chosen", project_id = "project", game_versions = new[] { "26.2" }, loaders = new[] { "minecraft" }, files = new[] { new { filename = "keep.zip", primary = true, url = "https://test.invalid/modrinth", hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(expected)) } } } };
            var cfFile = new { id = 42, fileName = "keep.zip", gameVersions = new[] { "26.2" }, isAvailable = true, downloadUrl = "https://test.invalid/curseforge", hashes = new[] { new { algo = 1, value = Convert.ToHexString(SHA1.HashData(expected)) } } };
            using var client = new HttpClient(new RouteHandler(uri => uri.AbsolutePath switch {
                "/version/chosen" => JsonSerializer.SerializeToUtf8Bytes(version),
                "/mods/42/files/42" => JsonSerializer.SerializeToUtf8Bytes(new { data = cfFile }),
                "/modrinth" or "/curseforge" => corrupt,
                _ => throw new Exception("Unexpected test route: " + uri.AbsolutePath)
            }));
            foreach (var source in new[] { "modrinth", "curseforge" })
            {
                try
                {
                    var project = new ModSearchResult { ProjectId = source == "modrinth" ? "project" : "42", SourceId = source, ProjectType = "resourcepack" };
                    if (source == "modrinth") await new ModrinthService(client, "https://test.invalid").DownloadAsync(project, instance, "26.2", "minecraft", "none", selectedVersionId: "chosen");
                    else await new CurseForgeService(client, "https://test.invalid", "test-key").DownloadAsync(project, instance, "26.2", "", "none", selectedFileId: 42);
                    throw new Exception("invalid checksum accepted");
                }
                catch (InvalidDataException) { }
                Require(File.ReadAllText(target) == "installed original", "previous file damaged by " + source);
                Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, "*.part*").Any(), "partial download was left behind");
            }
        });
        await Test("NeoForge distinguishes modern releases and patch versions", async () =>
        {
            using var client = new HttpClient(new RouteHandler(_ => Encoding.UTF8.GetBytes("<metadata><versioning><versions><version>21.1.5</version><version>26.1.0.1</version><version>26.1.2.2</version><version>26.2.0.93</version></versions></versioning></metadata>")));
            var service = new LoaderVersionService(client, new LauncherSettings());
            foreach (var pair in new[] { ("1.21.1", "21.1.5"), ("26.1", "26.1.0.1"), ("26.1.2", "26.1.2.2"), ("26.2", "26.2.0.93") })
                Require((await service.GetVersionsAsync("NeoForge", pair.Item1)).Single().Id == pair.Item2, "wrong NeoForge mapping for " + pair.Item1);
        });
        await Test("a damaged skin no longer hides valid skins and normalized slim variants use slim previews", async () =>
        {
            var directory = Path.Combine(root, "skins"); Directory.CreateDirectory(directory);
            using var bitmap = new SKBitmap(64, 64); bitmap.Erase(SKColors.White);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            var source = Path.Combine(root, "skin.png"); File.WriteAllBytes(source, png.ToArray());
            var service = new SkinLibraryService(directory);
            var valid = await service.ImportAsync(source, "SLIM");
            var broken = new SkinLibraryItem { Id = "broken", FileName = "broken.png" }; File.WriteAllText(Path.Combine(directory, broken.FileName), "not PNG");
            await service.SaveAsync(new[] { valid, broken });
            Require((await service.LoadAsync()).Single().Id == valid.Id, "bad skin hid the complete library");
            var pixel = new byte[4]; valid.Preview!.CopyPixels(new System.Windows.Int32Rect(0, 50, 1, 1), pixel, 4, 0);
            Require(valid.Variant == "slim" && pixel[3] == 0, "SLIM import preview used classic arms");
            using (var locked = new FileStream(Path.Combine(directory, "library.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { await service.DeleteAsync(valid, Array.Empty<SkinLibraryItem>()); throw new Exception("locked save should fail"); }
                catch (IOException) { }
                Require(File.Exists(valid.FilePath), "failed index save deleted the skin PNG");
            }
        });
        await Test("damaged instance metadata recovers a backup and an unreadable neighbor does not hide valid instances", async () =>
        {
            var directory = Path.Combine(root, "metadata-recovery"); var repo = new InstanceRepository(directory, Path.Combine(root, "shared"));
            var instance = await repo.CreateAsync(new GameInstance { Name = "Metadata" });
            File.WriteAllText(Path.Combine(instance.ModsPath, "keep.jar"), "data"); await repo.SaveAsync(instance);
            File.WriteAllText(Path.Combine(instance.DirectoryPath, "instance.json"), "{broken");
            var broken = Path.Combine(directory, "Broken"); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken, "instance.json"), "{invalid");
            Require((await repo.GetAllAsync()).Single().Id == instance.Id && repo.LoadWarnings.Count == 1, "backup/partial-library recovery failed");
            Require(File.ReadAllText(Path.Combine(instance.ModsPath, "keep.jar")) == "data", "metadata recovery changed mods");
            await repo.GetAllAsync();
            Require(Directory.GetFiles(broken, "*.corrupt-*").Length == 1, "repeated refresh generated duplicate corrupt backups");
        });
        await Test("ZIP traversal is rejected without publishing a partial imported instance", async () =>
        {
            var archive = Path.Combine(root, "traversal.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("escape");
            var repo = new InstanceRepository(Path.Combine(root, "traversal-instances"), Path.Combine(root, "shared"));
            try { await repo.ImportAsync(archive); throw new Exception("traversal accepted"); } catch (IOException) { }
            Require((await repo.GetAllAsync()).Count == 0, "partial traversal instance committed");
        });
        await Test("export and duplicate reject directory junctions and retain external data", async () =>
        {
            var directory = Path.Combine(root, "link-instances"); var repo = new InstanceRepository(directory, Path.Combine(root, "shared"));
            var instance = await repo.CreateAsync(new GameInstance { Name = "Junction" });
            var external = Path.Combine(root, "link-external"); Directory.CreateDirectory(external); File.WriteAllText(Path.Combine(external, "private.txt"), "keep outside");
            var link = Path.Combine(instance.DirectoryPath, "linked");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + external + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
            { await process.WaitForExitAsync(); Require(process.ExitCode == 0, "fixture junction creation failed"); }
            var archive = Path.Combine(root, "links.zip"); File.WriteAllText(archive, "previous");
            try { await repo.ExportAsync(instance, archive); throw new Exception("junction export accepted"); } catch (IOException) { }
            try { await repo.DuplicateAsync(instance); throw new Exception("junction copy accepted"); } catch (IOException) { }
            Require(File.ReadAllText(archive) == "previous" && File.ReadAllText(Path.Combine(external, "private.txt")) == "keep outside", "external/backup data changed");
            Require((await repo.GetAllAsync()).Count == 1, "partial junction copy was committed");
            // Remove the owned link while its target still exists; never recurse through it.
            Require(Path.GetDirectoryName(Path.GetFullPath(link)) == instance.DirectoryPath, "fixture link escaped the instance");
            Directory.Delete(link);
        });
        await Test("concurrent skin deletion retains the remaining skin and removes both requested entries", async () =>
        {
            var directory = Path.Combine(root, "skin-delete"); Directory.CreateDirectory(directory);
            var items = Enumerable.Range(0, 3).Select(i => new SkinLibraryItem { Id = i.ToString(), FileName = i + ".png", FilePath = Path.Combine(directory, i + ".png") }).ToArray();
            foreach (var item in items) File.WriteAllText(item.FilePath, "fixture");
            var service = new SkinLibraryService(directory); await service.SaveAsync(items);
            await Task.WhenAll(service.DeleteAsync(items[0], items.Skip(1)), service.DeleteAsync(items[1], new[] { items[0], items[2] }));
            Require(AtomicJsonFile.Read<List<SkinLibraryItem>>(Path.Combine(directory, "library.json"))!.Single().Id == "2", "concurrent deletions restored a stale entry");
            Require(File.Exists(items[2].FilePath), "remaining skin PNG was deleted");
        });
        await Test("pre-cancelled game launch creates no instance files and sends no requests", async () =>
        {
            var path = Path.Combine(root, "cancelled-launch");
            using var client = new HttpClient(new RouteHandler(_ => throw new Exception("cancelled launch sent HTTP")));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try
            {
                await new MinecraftService(client).InstallAndLaunchAsync(new GameInstance { DirectoryPath = path }, CmlLib.Core.Auth.MSession.CreateOfflineSession("Fixture"), new LauncherSettings(), new Progress<LaunchProgress>(), null, cancellation.Token);
                throw new Exception("cancelled launch accepted");
            }
            catch (OperationCanceledException) { }
            Require(!Directory.Exists(path), "cancelled launch wrote instance files");
        });
        await Test("instance names resembling internal folders remain visible and empty directories roundtrip", async () =>
        {
            var repo = new InstanceRepository(Path.Combine(root, "special-names"), Path.Combine(root, "shared"));
            var instance = await repo.CreateAsync(new GameInstance { Name = ".orbit-new-" + new string('a', 32) });
            Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, "empty", "nested"));
            Require((await repo.GetAllAsync()).Count == 1, "legitimate instance was hidden as an internal staging directory");
            var archive = Path.Combine(root, "empty-roundtrip.zip"); await repo.ExportAsync(instance, archive);
            var imported = await repo.ImportAsync(archive);
            Require(Directory.Exists(Path.Combine(imported.DirectoryPath, "empty", "nested")), "empty directories disappeared from exported/imported instance");
        });
        await Test("a shared-runtime migration error does not hide the player's instance", async () =>
        {
            var shared = Path.Combine(root, "blocked-shared"); File.WriteAllText(shared, "not a directory");
            var repo = new InstanceRepository(Path.Combine(root, "partial-library"), shared);
            var instance = await repo.CreateAsync(new GameInstance { Name = "Keep visible" });
            File.WriteAllText(Path.Combine(instance.ModsPath, "keep.jar"), "keep");
            Require((await repo.GetAllAsync()).Single().Id == instance.Id && repo.LoadWarnings.Count == 1, "migration failure hid the instance");
            Require(File.ReadAllText(Path.Combine(instance.ModsPath, "keep.jar")) == "keep", "migration failure changed player data");
        });
        if (failures.Count > 0) throw new AggregateException("Release audit regressions", failures);
        return passed;
    }
}
