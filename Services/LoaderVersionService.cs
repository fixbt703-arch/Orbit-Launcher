using System.Text.Json;
using System.Xml.Linq;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

public sealed class LoaderVersionService
{
    private readonly HttpClient _httpClient;
    private readonly LauncherSettings _settings;

    public LoaderVersionService(HttpClient httpClient, LauncherSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetVersionsAsync(
        string loader,
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameVersion) || string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
            return [];

        return loader switch
        {
            "Fabric" => await GetFabricVersionsAsync(gameVersion, cancellationToken),
            "Quilt" => await GetQuiltVersionsAsync(gameVersion, cancellationToken),
            "Forge" => await GetForgeVersionsAsync(gameVersion, cancellationToken),
            "NeoForge" => await GetNeoForgeVersionsAsync(gameVersion, cancellationToken),
            _ => []
        };
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> GetFabricVersionsAsync(string gameVersion, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await LauncherNetwork.GetMetadataAsync(_httpClient,
            $"{_settings.FabricMetaApi.TrimEnd('/')}/versions/loader/{Uri.EscapeDataString(gameVersion)}", cancellationToken));
        return document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("loader"))
            .Select(loader => new LoaderVersionInfo
            {
                Id = loader.GetProperty("version").GetString() ?? string.Empty,
                IsStable = loader.TryGetProperty("stable", out var stable) && stable.GetBoolean()
            })
            .Where(version => !string.IsNullOrWhiteSpace(version.Id))
            .ToList();
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> GetQuiltVersionsAsync(string gameVersion, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await LauncherNetwork.GetMetadataAsync(_httpClient,
            $"{_settings.QuiltMetaApi.TrimEnd('/')}/versions/loader/{Uri.EscapeDataString(gameVersion)}", cancellationToken));
        return document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("loader"))
            .Select(loader => new LoaderVersionInfo
            {
                Id = loader.GetProperty("version").GetString() ?? string.Empty,
                IsStable = !loader.TryGetProperty("separator", out _)
            })
            .Where(version => !string.IsNullOrWhiteSpace(version.Id))
            .ToList();
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> GetForgeVersionsAsync(string gameVersion, CancellationToken cancellationToken)
    {
        var allVersions = await GetMavenVersionsAsync(_settings.ForgeMavenMetadataApi, cancellationToken);
        var prefix = gameVersion + "-";
        return allVersions
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(version => new LoaderVersionInfo { Id = version[prefix.Length..] })
            .Reverse()
            .ToList();
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> GetNeoForgeVersionsAsync(string gameVersion, CancellationToken cancellationToken)
    {
        var parts = gameVersion.Split('.');
        if (parts.Length < 2 || !parts.All(part => int.TryParse(part, out _)))
            return [];

        var prefix = parts[0] == "1" ? string.Join('.', parts.Skip(1)) + "."
            : gameVersion + (parts.Length == 2 ? ".0." : ".");
        var allVersions = await GetMavenVersionsAsync(_settings.NeoForgeMavenMetadataApi, cancellationToken);
        return allVersions
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(version => new LoaderVersionInfo { Id = version, IsStable = !version.Contains("beta", StringComparison.OrdinalIgnoreCase) })
            .Reverse()
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetMavenVersionsAsync(string url, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(await LauncherNetwork.GetMetadataAsync(_httpClient, url, cancellationToken), writable: false);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        return document.Descendants("version")
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }
}
