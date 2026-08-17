using System.Text.Json;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

public sealed class VersionManifestService
{
    private readonly HttpClient _httpClient;
    private readonly string _manifestUrl;

    public VersionManifestService(HttpClient httpClient, string? manifestUrl = null)
    {
        _httpClient = httpClient;
        _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl)
            ? "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
            : manifestUrl.Trim();
    }

    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(bool includeSnapshots, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(_manifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var versions = new List<MinecraftVersionInfo>();
        foreach (var item in document.RootElement.GetProperty("versions").EnumerateArray())
        {
            var type = item.GetProperty("type").GetString() ?? "release";
            if (!includeSnapshots && type != "release")
                continue;

            versions.Add(new MinecraftVersionInfo
            {
                Id = item.GetProperty("id").GetString() ?? string.Empty,
                Type = type,
                ReleaseTime = item.GetProperty("releaseTime").GetDateTime()
            });
        }

        return versions;
    }
}
