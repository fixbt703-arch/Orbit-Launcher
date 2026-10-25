using System.IO.Compression;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class GlobalAuditBackendSmoke
{
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "global-backend"); Directory.CreateDirectory(root);
        var passed = 0;
        async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS global backend: " + name); }
        GameInstance Instance(string name, string loader = "Fabric") => new() { Name = name, DirectoryPath = Path.Combine(root, name), GameVersion = "1.21.1", Loader = loader };
        foreach (var nested in new[] { false, true })
            await Test("required dependency pins take precedence over " + (nested ? "nested" : "direct") + " optional branches", async () =>
            {
                var instance = Instance(nested ? "nested-optional" : "direct-optional");
                using var handler = new MetadataHandler(); using var client = new HttpClient(handler);
                handler.Project("root", Version("root", "root", "fabric", nested
                    ? [Dependency("left", "left"), Dependency("right", "right")]
                    : [Dependency("optional", "optional", "optional"), Dependency("right", "right")]));
                handler.Project("left", Version("left", "left", "fabric", [Dependency("optional", "optional", "optional")]));
                handler.Project("right", Version("right", "right", "fabric", [Dependency("lib", "lib-v1")]));
                handler.Project("optional", Version("optional", "optional", "fabric", [Dependency("lib", "lib-v2")]));
                handler.Project("lib", Version("lib", "lib-v1", "fabric", []));
                handler.ByVersion("lib-v2", Version("lib", "lib-v2", "fabric", []));
                await new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "1.21.1", "fabric", "all");
                var expected = nested ? new[] { "left.jar", "lib-v1.jar", "right.jar", "root.jar" } : new[] { "lib-v1.jar", "right.jar", "root.jar" };
                Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).Order().SequenceEqual(expected), "optional pin blocked or replaced the required graph");
                Require(new InstalledContentRegistry().Load(instance).Count == expected.Length, "omitted optional branch left a receipt");
            });
        await Test("optional metadata failure discards its entire required subtree", async () =>
        {
            var instance = Instance("optional-rollback");
            using var handler = new MetadataHandler(); using var client = new HttpClient(handler);
            handler.Project("root", Version("root", "root", "fabric", [Dependency("optional", "optional", "optional")]));
            handler.Project("optional", Version("optional", "optional", "fabric", [Dependency("lib", "lib-v2"), Dependency("missing", "missing")]));
            handler.Project("lib", Version("lib", "lib-v2", "fabric", []));
            await new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "1.21.1", "fabric", "all");
            Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).SequenceEqual(["root.jar"]), "failed optional subtree was published");
        });
        await Test("compatible optional branches retain their required and optional descendants", async () =>
        {
            var instance = Instance("optional-success");
            using var handler = new MetadataHandler(); using var client = new HttpClient(handler);
            handler.Project("root", Version("root", "root", "fabric", [Dependency("optional", "optional", "optional")]));
            handler.Project("optional", Version("optional", "optional", "fabric", [Dependency("lib", "lib"), Dependency("extra", "extra", "optional")]));
            handler.Project("lib", Version("lib", "lib", "fabric", []));
            handler.Project("extra", Version("extra", "extra", "fabric", []));
            await new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "1.21.1", "fabric", "all");
            Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).Order().SequenceEqual(["extra.jar", "lib.jar", "optional.jar", "root.jar"]), "compatible optional dependencies were omitted");
        });
        foreach (var loader in new[] { "Forge", "NeoForge", "Quilt" })
            await Test(loader + " repair installs the complete required dependency chain", async () =>
            {
                var instance = Instance("repair-" + loader, loader); Directory.CreateDirectory(instance.ModsPath);
                await File.WriteAllBytesAsync(Path.Combine(instance.ModsPath, "a.jar"), Jar("a", "a"));
                using var handler = new MetadataHandler(); using var client = new HttpClient(handler);
                var a = Version("a", "a", loader.ToLowerInvariant(), [Dependency("b", "b")]);
                handler.Hash(Jar("a", "a"), a);
                handler.Project("b", Version("b", "b", loader.ToLowerInvariant(), [Dependency("c", "c")]));
                handler.Project("c", Version("c", "c", loader.ToLowerInvariant(), []));
                var summary = await new ModCompatibilityRepairService(client, "https://fixture.invalid").RepairAsync(instance, new Progress<RepairUpdate>());
                Require(summary.Installed == 2 && summary.NeedsAttention == 0, "repair reported an incomplete dependency chain as complete");
                Require(Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).Order().SequenceEqual(["a.jar", "b.jar", "c.jar"]), "transitive dependency missing");
            });
        await Test("repair resolves version-only required dependencies and stops dependency cycles", async () =>
        {
            var instance = Instance("repair-version-only", "Forge"); Directory.CreateDirectory(instance.ModsPath);
            await File.WriteAllBytesAsync(Path.Combine(instance.ModsPath, "a.jar"), Jar("a", "a"));
            using var handler = new MetadataHandler(); using var client = new HttpClient(handler);
            handler.Hash(Jar("a", "a"), Version("a", "a", "forge", [Dependency(null, "b")]));
            handler.Project("b", Version("b", "b", "forge", [Dependency("a", "a")]));
            var summary = await new ModCompatibilityRepairService(client, "https://fixture.invalid").RepairAsync(instance, new Progress<RepairUpdate>());
            Require(summary.Installed == 1 && summary.NeedsAttention == 0 && File.Exists(Path.Combine(instance.ModsPath, "b.jar")), "version-only dependency was skipped or cycle repeated");
        });
        await Test("ZIP round trip retains nested custom icon", async () =>
        {
            var repo = new InstanceRepository(Path.Combine(root, "icons"), Path.Combine(root, "shared"));
            var original = await repo.CreateAsync(new() { Name = "Icon", IconFile = Path.Combine(".orbit", "icon.png") });
            await File.WriteAllBytesAsync(original.IconPath, [1, 2, 3]); await repo.SaveAsync(original);
            var archive = Path.Combine(root, "icon.zip"); await repo.ExportAsync(original, archive);
            var imported = await repo.ImportAsync(archive);
            Require(File.Exists(imported.IconPath) && (await File.ReadAllBytesAsync(imported.IconPath)).SequenceEqual(new byte[] { 1, 2, 3 }), "import flattened the nested icon path");
        });
        await Test("ZIP import clears icon paths escaping the imported content", async () =>
        {
            var archive = Path.Combine(root, "unsafe-icon.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("instance.json").Open()))
                writer.Write(JsonSerializer.Serialize(new GameInstance { Name = "Unsafe icon", IconFile = "../outside.png" }));
            var repo = new InstanceRepository(Path.Combine(root, "unsafe-icons"), Path.Combine(root, "shared"));
            Require((await repo.ImportAsync(archive)).IconFile == "", "outside icon path survived import");
        });
        return passed;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object Dependency(string? project, string version, string type = "required") => new { project_id = project, version_id = version, dependency_type = type };
    private static object Version(string project, string id, string loader, object[] dependencies) => new
    {
        id, project_id = project, game_versions = new[] { "1.21.1" }, loaders = new[] { loader }, version_type = "release", dependencies,
        files = new[] { new { filename = id + ".jar", primary = true, url = "https://cdn.modrinth.com/files/" + id + ".jar", hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(Jar(project, id))) } } }
    };
    private static byte[] Jar(string project, string version)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("fabric.mod.json"); entry.LastWriteTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open()); writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id = project, version }));
        }
        return bytes.ToArray();
    }
    private sealed class MetadataHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _routes = [];
        public void ByVersion(string id, object metadata)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(metadata); _routes["/version/" + id] = bytes;
            using var json = JsonDocument.Parse(bytes);
            _routes["/files/" + id + ".jar"] = Jar(json.RootElement.GetProperty("project_id").GetString()!, id);
        }
        public void Project(string project, object metadata)
        {
            _routes["/project/" + project + "/version"] = JsonSerializer.SerializeToUtf8Bytes(new[] { metadata });
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(metadata));
            ByVersion(json.RootElement.GetProperty("id").GetString()!, metadata);
        }
        public void Hash(byte[] bytes, object metadata) => _routes["/version_file/" + Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()] = JsonSerializer.SerializeToUtf8Bytes(metadata);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var found = _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound) { RequestMessage = request, Content = new ByteArrayContent(bytes ?? []) });
        }
    }
}
