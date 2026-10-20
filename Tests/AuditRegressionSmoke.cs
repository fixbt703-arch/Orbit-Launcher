using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CmlLib.Core.Auth;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class AuditRegressionSmoke
{
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "audit"); Directory.CreateDirectory(root);
        int passed = 0;
        async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS audit: " + name); }
        GameInstance Instance(string name) => new() { Name = name, DirectoryPath = Path.Combine(root, name), GameVersion = "26.2", Loader = "Fabric" };
        await Test("pinned dependencies replace matching mod IDs, retain backups and reject disabled dependencies", async () =>
        {
            var instance = Instance("pinned"); Directory.CreateDirectory(instance.ModsPath);
            await File.WriteAllBytesAsync(Path.Combine(instance.ModsPath, "dep-old.jar"), Jar("dep", "1.0"));
            using var handler = new CatalogHandler(); using var client = new HttpClient(handler);
            await new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "26.2", "fabric", "required");
            Require(handler.Requests.Contains("/version/dep-pinned") && !handler.Requests.Contains("/project/dep/version"), "dependency pin lost");
            Require(!File.Exists(Path.Combine(instance.ModsPath, "dep-old.jar")) && File.Exists(Path.Combine(instance.ModsPath, "dep-pinned.jar")), "duplicate dependency retained");
            Require(Directory.EnumerateFiles(Path.Combine(instance.DirectoryPath, ".orbit", "mod-backups"), "dep-old.jar", SearchOption.AllDirectories).Count() == 1, "old dependency not backed up");
            Require(new InstalledContentRegistry().Load(instance).Count == 2, "dependency receipt missing");
            File.Move(Path.Combine(instance.ModsPath, "dep-pinned.jar"), Path.Combine(instance.ModsPath, "dep-pinned.jar.disabled"));
            await MustFail(() => new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "26.2", "fabric", "required"));
            Require(File.Exists(Path.Combine(instance.ModsPath, "dep-pinned.jar.disabled")), "disabled dependency changed");
        });
        await Test("incompatible dependency pins fail before downloads or changes", async () =>
        {
            var instance = Instance("conflict"); Directory.CreateDirectory(instance.ModsPath);
            await File.WriteAllTextAsync(Path.Combine(instance.ModsPath, "keep.jar"), "keep");
            using var handler = new CatalogHandler { Conflict = true }; using var client = new HttpClient(handler);
            await MustFail(() => new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "26.2", "fabric", "required"));
            Require(!handler.Requests.Any(path => path.StartsWith("/files/")) && Directory.GetFiles(instance.ModsPath).Length == 1, "conflict changed mods");
        });
        await Test("a failed dependency download preserves all existing mods", async () =>
        {
            var instance = Instance("bad-hash"); Directory.CreateDirectory(instance.ModsPath);
            var original = Jar("root", "old"); await File.WriteAllBytesAsync(Path.Combine(instance.ModsPath, "root.jar"), original);
            using var handler = new CatalogHandler { BadDependency = true }; using var client = new HttpClient(handler);
            await MustFail(() => new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "26.2", "fabric", "required"));
            Require(original.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(instance.ModsPath, "root.jar"))) && Directory.GetFiles(instance.ModsPath).Length == 1, "failed batch changed installed files");
        });
        await Test("cached Microsoft sessions refresh and a network failure never returns a stale token", async () =>
        {
            int refreshes = 0; bool fail = false;
            using var http = new HttpClient();
            var auth = new AuthService(http, () =>
            {
                refreshes++;
                if (fail) throw new HttpRequestException("offline fixture");
                return Task.FromResult(new MSession { Username = "AuditPlayer", AccessToken = "token-" + refreshes });
            });
            var first = await auth.GetRequiredSessionAsync(new()); var second = await auth.GetRequiredSessionAsync(new());
            Require(first.AccessToken != second.AccessToken && refreshes == 2, "cached token bypassed refresh");
            fail = true; await MustFail(() => auth.GetRequiredSessionAsync(new()));
            Require(auth.IsSignedIn && auth.DisplayName == "AuditPlayer", "temporary failure cleared account");
        });
        await Test("optional download failure does not block the main mod", async () =>
        {
            var instance = Instance("optional");
            using var handler = new CatalogHandler { OptionalDependency = true, BadDependency = true }; using var client = new HttpClient(handler);
            await new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "26.2", "fabric", "all");
            Require(File.Exists(Path.Combine(instance.ModsPath, "root.jar")) && !File.Exists(Path.Combine(instance.ModsPath, "dep-pinned.jar")), "optional failure blocked root or published broken bytes");
        });
        await Test("registry commit failure rolls back every replaced mod", async () =>
        {
            var instance = Instance("rollback"); Directory.CreateDirectory(instance.ModsPath);
            foreach (var id in new[] { "root", "dep" }) await File.WriteAllBytesAsync(Path.Combine(instance.ModsPath, id + "-old.jar"), Jar(id, "old"));
            Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, ".orbit", "installed-content.json"));
            var requests = new[] { "root", "dep" }.Select(id => new CatalogModInstaller.Request(id, async directory =>
            { var path = Path.Combine(directory, id + "-new.jar"); await File.WriteAllBytesAsync(path, Jar(id, "new")); return path; })).ToArray();
            await MustFail(() => CatalogModInstaller.InstallAsync(instance, "curseforge", requests, default));
            foreach (var id in new[] { "root", "dep" }) Require(Jar(id, "old").SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(instance.ModsPath, id + "-old.jar"))) && !File.Exists(Path.Combine(instance.ModsPath, id + "-new.jar")), "incomplete rollback");
        });
        await Test("a damaged skin index reports failure while retaining PNG files", async () =>
        {
            var directory = Path.Combine(root, "damaged-library"); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "library.json"), "{damaged");
            await File.WriteAllTextAsync(Path.Combine(directory, "keep.png"), "retained fixture");
            await MustFail(() => new SkinLibraryService(directory).LoadAsync());
            Require(File.Exists(Path.Combine(directory, "keep.png")), "skin removed on index failure");
        });
        await Test("Java selection follows the game manifest and rejects an older explicit runtime", async () =>
        {
            var game = Path.Combine(root, "minecraft"); var version = Path.Combine(game, "versions", "26.2"); Directory.CreateDirectory(version);
            await File.WriteAllTextAsync(Path.Combine(version, "26.2.json"), "{\"javaVersion\":{\"majorVersion\":25,\"component\":\"java-runtime-epsilon\"}}");
            string Java(string component, int major)
            {
                var directory = Path.Combine(game, "runtime", component); Directory.CreateDirectory(Path.Combine(directory, "bin"));
                File.WriteAllText(Path.Combine(directory, "release"), "JAVA_VERSION=\"" + major + ".0.1\"");
                var path = Path.Combine(directory, "bin", "java.exe"); File.WriteAllText(path, "not executed"); return path;
            }
            var old = Java("java-runtime-delta", 21); var current = Java("java-runtime-epsilon", 25);
            var instance = Instance("java");
            var method = typeof(MinecraftService).GetMethod("ResolveJavaPath", BindingFlags.NonPublic | BindingFlags.Static)!;
            Require((string?)method.Invoke(null, [instance, game]) == current, "wrong Java selected");
            instance.JavaPath = old;
            await MustFail(() => Task.FromResult(method.Invoke(null, [instance, game])));
        });
        await Test("NeoForge matches complete artifact versions and game inheritance", async () =>
        {
            var versions = Path.Combine(root, "profiles"); Directory.CreateDirectory(versions);
            foreach (var (name, game, loader) in new[] { ("a-neoforge-21.1.10", "1.21.1", "21.1.10"), ("b-neoforge-21.1.1", "1.21.2", "21.1.1"), ("z-neoforge-21.1.1", "1.21.1", "21.1.1") })
            {
                var directory = Path.Combine(versions, name); Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new { id = name, inheritsFrom = game, libraries = new[] { new { name = "net.neoforged:neoforge:" + loader } } }));
            }
            var method = typeof(MinecraftService).GetMethod("FindNeoForgeProfile", BindingFlags.NonPublic | BindingFlags.Static)!;
            Require((string?)method.Invoke(null, [new GameInstance { GameVersion = "1.21.1", LoaderVersion = "21.1.1" }, versions]) == "z-neoforge-21.1.1", "wrong profile selected");
        });
        await Test("simultaneous game pickers receive only their own instance's packs", async () =>
        {
            var a = Instance("picker-a"); var b = Instance("picker-b");
            foreach (var instance in new[] { a, b })
            {
                var downloads = Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "datapacks"); Directory.CreateDirectory(downloads);
                using var zip = ZipFile.Open(Path.Combine(downloads, instance.Name + ".zip"), ZipArchiveMode.Create);
                using var writer = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open()); writer.Write("{}");
            }
            using var bridgeA = new DatapackWorldCreationBridge(a.DirectoryPath); using var bridgeB = new DatapackWorldCreationBridge(b.DirectoryPath);
            var pickerA = Path.Combine(bridgeA.TemporaryDirectory, "mcworld-test"); var pickerB = Path.Combine(bridgeB.TemporaryDirectory, "mcworld-test");
            Directory.CreateDirectory(pickerA); Directory.CreateDirectory(pickerB);
            for (int i = 0; i < 40 && (!File.Exists(Path.Combine(pickerA, a.Name + ".zip")) || !File.Exists(Path.Combine(pickerB, b.Name + ".zip"))); i++) await Task.Delay(50);
            Require(Directory.GetFiles(pickerA).Select(Path.GetFileName).SequenceEqual([a.Name + ".zip"]) && Directory.GetFiles(pickerB).Select(Path.GetFileName).SequenceEqual([b.Name + ".zip"]), "packs mixed across games");
        });
        await Test("launch diagnostics retain output while redacting session credentials", async () =>
        {
            string path;
            using (var log = new GameLaunchLog(Path.Combine(root, "logs"), "private-fixture-token"))
            { path = log.FilePath; log.Write("Loading Fabric"); log.Write("--accessToken private-fixture-token Authorization: Bearer another-token"); }
            var text = await File.ReadAllTextAsync(path);
            Require(text.Contains("Loading Fabric") && !text.Contains("private-fixture-token") && !text.Contains("another-token"), "credentials entered diagnostics");
        });
        return passed;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task MustFail(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected failure"); }
    private static byte[] Jar(string id, string version)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("fabric.mod.json"); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open()); writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id, version }));
        }
        return bytes.ToArray();
    }
    private sealed class CatalogHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool Conflict { get; init; }
        public bool BadDependency { get; init; }
        public bool OptionalDependency { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; Requests.Add(path);
            object Dep(string project, string version) => new { project_id = project, version_id = version, dependency_type = OptionalDependency ? "optional" : "required" };
            object Version(string project, string id, object[] dependencies) => new { id, project_id = project, game_versions = new[] { "26.2" }, loaders = new[] { "fabric" }, dependencies,
                files = new[] { new { filename = id + ".jar", primary = true, url = "https://fixture.invalid/files/" + id + ".jar", hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(Jar(project, id))) } } } };
            var bytes = path switch
            {
                "/project/root/version" => JsonSerializer.SerializeToUtf8Bytes(new[] { Version("root", "root", Conflict ? [Dep("left", "left"), Dep("right", "right")] : [Dep("dep", "dep-pinned")]) }),
                "/version/left" => JsonSerializer.SerializeToUtf8Bytes(Version("left", "left", [Dep("dep", "dep-pinned")])),
                "/version/right" => JsonSerializer.SerializeToUtf8Bytes(Version("right", "right", [Dep("dep", "dep-other")])),
                "/version/dep-pinned" => JsonSerializer.SerializeToUtf8Bytes(Version("dep", "dep-pinned", [])),
                "/files/root.jar" => Jar("root", "root"),
                "/files/dep-pinned.jar" => BadDependency ? [0, 1, 2] : Jar("dep", "dep-pinned"),
                _ => throw new Exception("Unexpected fixture request: " + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request });
        }
    }
}
