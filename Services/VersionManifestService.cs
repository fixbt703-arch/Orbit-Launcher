using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

public sealed class VersionManifestService
{
	private readonly HttpClient _httpClient;

	private readonly string _manifestUrl;

	public VersionManifestService(HttpClient httpClient, string? manifestUrl = null)
	{
		_httpClient = httpClient;
		_manifestUrl = (string.IsNullOrWhiteSpace(manifestUrl) ? "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" : manifestUrl.Trim());
	}

	public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(bool includeSnapshots, CancellationToken cancellationToken = default(CancellationToken))
	{
		using HttpResponseMessage response = await _httpClient.GetAsync(_manifestUrl, cancellationToken);
		response.EnsureSuccessStatusCode();
		IReadOnlyList<MinecraftVersionInfo> result;
		await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
		{
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
			List<MinecraftVersionInfo> list = new List<MinecraftVersionInfo>();
			foreach (JsonElement item in jsonDocument.RootElement.GetProperty("versions").EnumerateArray())
			{
				string text = item.GetProperty("type").GetString() ?? "release";
				if (includeSnapshots || !(text != "release"))
				{
					list.Add(new MinecraftVersionInfo
					{
						Id = (item.GetProperty("id").GetString() ?? string.Empty),
						Type = text,
						ReleaseTime = item.GetProperty("releaseTime").GetDateTime()
					});
				}
			}
			result = list;
		}
		return result;
	}
}
