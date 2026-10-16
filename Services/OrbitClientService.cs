using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class OrbitClientService(HttpClient httpClient)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallGates = new(StringComparer.OrdinalIgnoreCase);
    public const string GameVersion = "26.2";
    public const string ClientVersion = "0.18.4";
    public const string JarName = "orbit-client-26.2.jar";
    public static bool Supports(string gameVersion, string loader) =>
        gameVersion == GameVersion && string.Equals(loader, "Fabric", StringComparison.OrdinalIgnoreCase);

    public async Task EnsureInstalledAsync(GameInstance instance, IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!instance.OrbitClientEnabled) return;
        var gate = InstallGates.GetOrAdd(Path.GetFullPath(instance.DirectoryPath), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { await EnsureInstalledCoreAsync(instance, progress, cancellationToken); }
        finally { gate.Release(); }
    }

    private async Task EnsureInstalledCoreAsync(GameInstance instance, IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Supports(instance.GameVersion, instance.Loader))
            throw new InvalidOperationException("Orbit Client поддерживает только Minecraft 26.2 + Fabric.");
        if (!Version.TryParse(instance.LoaderVersion, out var loaderVersion) || loaderVersion < new Version(0, 19, 3))
            throw new InvalidOperationException("Orbit Client требует Fabric Loader 0.19.3 или новее.");

        Directory.CreateDirectory(instance.ModsPath);
        var receipt = Path.Combine(instance.DirectoryPath, ".orbit", "orbit-client-version.txt");
        var alreadyInstalled = File.Exists(receipt);
        await using var manifest = Resource("OrbitLauncher.OrbitClient.Components");
        var components = await JsonSerializer.DeserializeAsync<List<Component>>(manifest, JsonDefaults.Options, cancellationToken)
                         ?? throw new InvalidDataException("Orbit Client component manifest is empty.");

        var activeMods = ReadActiveMods(instance.ModsPath);
        var ownPath = Path.Combine(instance.ModsPath, JarName);
        if (activeMods.TryGetValue("orbit_client", out var existingCore) && !string.Equals(existingCore, ownPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("В сборке уже есть другой файл Orbit Client. Уберите дубликат перед запуском.");

        for (var index = 0; index < components.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = components[index];
            var required = component.Required || component.ModId == "fabric-api";
            progress?.Report(new LaunchProgress(index * 100d / (components.Count + 1), "Orbit Client • " + component.Name));
            // User-managed upgrades and disabled optional optimizers are preserved after installation.
            if (activeMods.ContainsKey(component.ModId)) continue;
            if (alreadyInstalled && !required) continue;
            var destination = Path.Combine(instance.ModsPath, component.FileName);
            if (Path.GetFileName(component.FileName) != component.FileName || !component.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid Orbit Client component filename.");
            if (File.Exists(destination + ".disabled"))
            {
                if (required) throw new InvalidOperationException($"Включите {component.Name} в разделе модов: этот компонент необходим Orbit Client.");
                continue;
            }
            if (!File.Exists(destination) || !await HashMatchesAsync(destination, component.Sha512, cancellationToken))
                await DownloadAsync(component, destination, cancellationToken);
            await new InstalledContentRegistry().MarkAsync(instance,
                new ModSearchResult { ProjectId = component.ProjectId, SourceId = "modrinth", Title = component.Name, ProjectType = "mod" },
                CatalogInstallState.Installed, destination);
        }

        progress?.Report(new LaunchProgress(95, "Orbit Client • " + ClientVersion));
        await using var source = Resource("OrbitLauncher.OrbitClient.Jar");
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes, cancellationToken);
        var packaged = bytes.ToArray();
        if (!File.Exists(ownPath) || !SHA256.HashData(await File.ReadAllBytesAsync(ownPath, cancellationToken)).SequenceEqual(SHA256.HashData(packaged)))
        {
            var temporary = ownPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporary, packaged, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, ownPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);
        var receiptTemporary = receipt + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(receiptTemporary, ClientVersion, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(receiptTemporary, receipt, true);
        }
        finally { if (File.Exists(receiptTemporary)) File.Delete(receiptTemporary); }
        progress?.Report(new LaunchProgress(100, "Orbit Client готов • Right Shift"));
    }

    private async Task DownloadAsync(Component component, string destination, CancellationToken token)
    {
        var uri = new Uri(component.Url);
        if (uri.Scheme != "https" || uri.Host != "cdn.modrinth.com") throw new InvalidDataException("Invalid component source.");
        var temporary = destination + ".orbit-download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output, token);
            if (!await HashMatchesAsync(temporary, component.Sha512, token))
                throw new InvalidDataException($"Не совпала контрольная сумма {component.Name}. Повторите установку.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ReadActiveMods(string modsPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(modsPath, "*.jar"))
        {
            string? id;
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry("fabric.mod.json");
                if (entry is null) continue;
                using var stream = entry.Open();
                using var json = JsonDocument.Parse(stream);
                if (!json.RootElement.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String) continue;
                id = idValue.GetString();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException) { continue; }
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!result.TryAdd(id, path) && id.Equals("orbit_client", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("В сборке несколько файлов Orbit Client. Уберите дубликаты перед запуском.");
        }
        return result;
    }

    private static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("В лаунчере отсутствуют файлы Orbit Client. Выполните client-build.bat, затем build.bat.");

    private sealed record Component(string Name, string ProjectId, string ModId, string FileName, string Url, string Sha512, bool Required = false);
}
