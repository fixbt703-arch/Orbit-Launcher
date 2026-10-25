using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class ModDownloadAuditSmoke
{
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "mod-download-audit");
        foreach (var versionOnly in new[] { false, true })
        {
            var instance = new GameInstance { Name = "Pinned dependencies", DirectoryPath = Path.Combine(root, "replacement-" + versionOnly), GameVersion = "1.21.1", Loader = "Fabric" };
            using var handler = new Routes();
            handler.Version("root", "root", [Dependency("library"), Dependency("consumer")]);
            handler.Version("library", "library-new", [Dependency("shared", "shared-new")]);
            handler.Version("library", "library-old", [Dependency("shared", "shared-old")], latest: false);
            handler.Version("consumer", "consumer", [Dependency(versionOnly ? null : "library", "library-old")]);
            handler.Version("shared", "shared-new", []);
            handler.Version("shared", "shared-old", [], latest: false);
            using var client = new HttpClient(handler);
            await new ModrinthService(client, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, instance, "1.21.1", "fabric", "required");
            var files = Directory.GetFiles(instance.ModsPath).Select(Path.GetFileName).Order().ToArray();
            if (!files.SequenceEqual(new[] { "consumer.jar", "library-old.jar", "root.jar", "shared-old.jar" }))
                throw new Exception("Superseded library metadata left the wrong dependency graph: " + string.Join(", ", files));
            Console.WriteLine("PASS mod download: pinned library discards constraints of superseded latest release (version-only=" + versionOnly + ")");
        }
        var conflict = new GameInstance { Name = "Conflicting pins", DirectoryPath = Path.Combine(root, "conflict"), GameVersion = "1.21.1", Loader = "Fabric" };
        Directory.CreateDirectory(conflict.ModsPath);
        var original = Path.Combine(conflict.ModsPath, "keep.jar"); await File.WriteAllTextAsync(original, "original");
        using var conflictingHandler = new Routes();
        conflictingHandler.Version("root", "root", [Dependency("library", "library-new"), Dependency("consumer")]);
        conflictingHandler.Version("library", "library-new", []);
        conflictingHandler.Version("library", "library-old", [], latest: false);
        conflictingHandler.Version("consumer", "consumer", [Dependency("library", "library-old")]);
        using var conflictingClient = new HttpClient(conflictingHandler);
        try
        {
            await new ModrinthService(conflictingClient, "https://fixture.invalid").DownloadAsync(new() { ProjectId = "root", ProjectType = "mod" }, conflict, "1.21.1", "fabric", "required");
            throw new Exception("Conflicting active dependency pins were accepted");
        }
        catch (InvalidDataException) { }
        if (Directory.GetFiles(conflict.ModsPath).Length != 1 || await File.ReadAllTextAsync(original) != "original")
            throw new Exception("Conflicting dependency pins changed existing files");
        Console.WriteLine("PASS mod download: conflicting active pins preserve installed files");
        return 3;
    }

    private static object Dependency(string? project, string? version = null) => new { project_id = project, version_id = version, dependency_type = "required" };
    private sealed class Routes : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _routes = [];
        public void Version(string project, string version, object[] dependencies, bool latest = true)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                var entry = zip.CreateEntry("fabric.mod.json"); entry.LastWriteTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open()); writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id = project, version }));
            }
            var jar = bytes.ToArray();
            var metadata = new { id = version, project_id = project, game_versions = new[] { "1.21.1" }, loaders = new[] { "fabric" }, dependencies,
                files = new[] { new { filename = version + ".jar", primary = true, url = "https://fixture.invalid/files/" + version + ".jar", hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(jar)) } } } };
            _routes["/version/" + version] = JsonSerializer.SerializeToUtf8Bytes(metadata);
            if (latest) _routes["/project/" + project + "/version"] = JsonSerializer.SerializeToUtf8Bytes(new[] { metadata });
            _routes["/files/" + version + ".jar"] = jar;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var found = _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new ByteArrayContent(bytes ?? []) });
        }
    }
}
