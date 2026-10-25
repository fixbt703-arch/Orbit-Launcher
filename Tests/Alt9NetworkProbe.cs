using System.IO;
using System.Net.Http;
using OrbitLauncher.Services;

internal static class Alt9NetworkProbe
{
    public static async Task CreateInstancesAsync(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Probe output must be a new directory.");
        Directory.CreateDirectory(root);
        using var http = LauncherNetwork.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var versions = await new VersionManifestService(http).GetVersionsAsync(false, deadline.Token);
        if (!versions.Any(version => version.Id == "26.2")) throw new Exception("Minecraft 26.2 absent from live manifest");
        var loaders = await new LoaderVersionService(http, new OrbitLauncher.Models.LauncherSettings()).GetVersionsAsync("Fabric", "26.2", deadline.Token);
        if (loaders.Count == 0) throw new Exception("Live Fabric loader list is empty");
        var repository = new InstanceRepository(Path.Combine(root, "instances"), Path.Combine(root, "shared"));
        foreach (string loader in new[] { "Vanilla", "Fabric" })
            await repository.CreateAsync(new OrbitLauncher.Models.GameInstance { Name = "ALT9 " + loader, GameVersion = "26.2", Loader = loader,
                LoaderVersion = loader == "Fabric" ? loaders[0].Id : "" });
        var saved = await repository.GetAllAsync();
        if (saved.Count != 2 || repository.LoadWarnings.Count != 0 || saved.Any(instance => !Directory.Exists(instance.ModsPath)))
            throw new Exception("New instance definitions did not reopen");
        Console.WriteLine("PASS live instance creation: official manifest, Fabric loaders, new Vanilla/Fabric definitions and reopen; user library untouched.");
        var fixture = Path.Combine(root, "zapret-fixture");
        Directory.CreateDirectory(Path.Combine(fixture, "bin")); Directory.CreateDirectory(Path.Combine(fixture, "lists"));
        await File.WriteAllTextAsync(Path.Combine(fixture, "service.bat"), "fixture only");
        await File.WriteAllTextAsync(Path.Combine(fixture, "bin", "winws.exe"), "fixture only");
        await File.WriteAllTextAsync(Path.Combine(fixture, "lists", "list-exclude-user.txt"), "# preserved user entry\ndomain.example.abc\n");
        var result = await ZapretCompatibilityService.ApplyAsync(fixture, deadline.Token, Path.Combine(root, "compatibility-backups"));
        if (!result.Changed || result.BackupPath is null) throw new Exception("Compatibility repair fixture failed");
        Console.WriteLine("PASS compatibility repair: actual service generated the backed-up exclusion list in an isolated fixture.");
    }

    public static async Task RunAsync()
    {
        using var http = LauncherNetwork.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.6");
        string[] urls = [
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            "https://meta.fabricmc.net/v2/versions/loader/26.2",
            "https://meta.quiltmc.org/v3/versions/loader/1.20.1",
            "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
            "https://api.modrinth.com/v2/tag/game_version",
            "https://www.curseforge.com/minecraft",
            "https://skinsystem.ely.by/skins/ErickSkrauch.png?version=2",
            "https://authserver.ely.by/",
            "https://account.ely.by/",
            "https://api.minecraftservices.com/minecraft/profile",
            "https://login.microsoftonline.com/consumers/v2.0/.well-known/openid-configuration",
            "https://github.com/fixbt703-arch/Orbit-Launcher/releases/latest"
        ];
        int failures = 0;
        foreach (bool compatible in new[] { false, true })
        foreach (string url in urls)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Options.Set(LauncherNetwork.CompatibilityTransportKey, compatible);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                await response.Content.CopyToAsync(Stream.Null, deadline.Token);
                Console.WriteLine($"PASS TLS: {new Uri(url).Host}; compatible={compatible}; status={(int)response.StatusCode}; {watch.ElapsedMilliseconds}ms");
                if ((int)response.StatusCode >= 500) failures++;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            { failures++; Console.WriteLine($"FAIL TLS: {new Uri(url).Host}; compatible={compatible}; {ex.GetType().Name}; {watch.ElapsedMilliseconds}ms"); }
        }
        if (failures > 0) throw new Exception($"{failures} public network probes failed");
    }
}
