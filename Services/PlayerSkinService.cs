using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class PlayerSkinService
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, CachedHead> _heads = new(StringComparer.OrdinalIgnoreCase);
    private sealed record CachedHead(PlayerHeadImage Head, DateTime UpdatedAt);

    public PlayerSkinService(HttpClient httpClient, string? cacheDirectory = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory ?? Path.Combine(AppPaths.Cache, "player-heads"));
    }

    public PlayerHeadImage? GetCachedHead(string username) => ReadCache(username)?.Head;

    private CachedHead? ReadCache(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        if (_heads.TryGetValue(username, out var cached)) return cached;
        try
        {
            string path = CachePath(username);
            if (!File.Exists(path)) return null;
            var image = Decode(File.ReadAllBytes(path));
            if (image.PixelWidth != 8 || image.PixelHeight != 8) return null;
            cached = new CachedHead(new PlayerHeadImage(image), File.GetLastWriteTimeUtc(path));
            _heads[username] = cached;
            return cached;
        }
        catch { return null; } // A missing or damaged optional cache must never break the account UI.
    }

    public async Task<PlayerHeadImage?> GetHeadAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var cached = ReadCache(username);
        if (cached is not null && DateTime.UtcNow - cached.UpdatedAt < TimeSpan.FromMinutes(10)) return cached.Head;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var head = await DownloadHeadAsync(username, deadline.Token);
                if (head is not null) { Store(username, head); return head; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* Keep the last working head when public profile services are unavailable. */ }
            if (attempt == 0)
            {
                try { await Task.Delay(250, deadline.Token); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { break; }
            }
        }
        return cached?.Head;
    }

    private async Task<PlayerHeadImage?> DownloadHeadAsync(string username, CancellationToken cancellationToken)
    {
        using var nameResponse = await _httpClient.GetAsync("https://api.mojang.com/users/profiles/minecraft/" + Uri.EscapeDataString(username), cancellationToken);
        if (!nameResponse.IsSuccessStatusCode) return null;
        using var nameDocument = JsonDocument.Parse(await nameResponse.Content.ReadAsStringAsync(cancellationToken));
        if (!nameDocument.RootElement.TryGetProperty("id", out var idNode) || string.IsNullOrWhiteSpace(idNode.GetString())) return null;
        using var profileResponse = await _httpClient.GetAsync("https://sessionserver.mojang.com/session/minecraft/profile/" + Uri.EscapeDataString(idNode.GetString()!), cancellationToken);
        if (!profileResponse.IsSuccessStatusCode) return null;
        using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync(cancellationToken));
        if (!profile.RootElement.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array) return null;
        foreach (var property in properties.EnumerateArray())
        {
            if (!property.TryGetProperty("name", out var name) || name.GetString() != "textures" || !property.TryGetProperty("value", out var encoded)) continue;
            using var textures = JsonDocument.Parse(Convert.FromBase64String(encoded.GetString() ?? ""));
            if (!textures.RootElement.TryGetProperty("textures", out var root) || !root.TryGetProperty("SKIN", out var skin) || !skin.TryGetProperty("url", out var url)) return null;
            if (!Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
            var image = Decode(await _httpClient.GetByteArrayAsync(uri, cancellationToken));
            return image.PixelWidth == 64 && (image.PixelHeight == 32 || image.PixelHeight == 64)
                ? new PlayerHeadImage(ComposeHead(image)) : null;
        }
        return null;
    }

    // A successful upload already has the exact texture: don't wait for public profile caches to update.
    public void RememberSkin(string username, byte[] pngBytes)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        var image = Decode(pngBytes);
        if (image.PixelWidth != 64 || (image.PixelHeight != 32 && image.PixelHeight != 64)) return;
        Store(username, new PlayerHeadImage(ComposeHead(image)));
    }

    private void Store(string username, PlayerHeadImage head)
    {
        _heads[username] = new CachedHead(head, DateTime.UtcNow);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            string path = CachePath(username);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(head.Image));
            using (var stream = File.Create(temporary)) encoder.Save(stream);
            File.Move(temporary, path, overwrite: true);
        }
        catch { /* Cache writes are optional; the decoded image remains usable in memory. */ }
        finally { if (temporary is not null) { try { File.Delete(temporary); } catch { } } }
    }

    private string CachePath(string username) => Path.Combine(_cacheDirectory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToLowerInvariant()))) + ".png");

    private static BitmapImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
	private static BitmapSource ComposeHead(BitmapSource skin)
	{
		FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(new CroppedBitmap(skin, new Int32Rect(8, 8, 8, 8)), PixelFormats.Bgra32, null, 0.0);
		FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(new CroppedBitmap(skin, new Int32Rect(40, 8, 8, 8)), PixelFormats.Bgra32, null, 0.0);
		byte[] array = new byte[256];
		byte[] array2 = new byte[array.Length];
		formatConvertedBitmap.CopyPixels(array, 32, 0);
		formatConvertedBitmap2.CopyPixels(array2, 32, 0);
		for (int i = 0; i < array.Length; i += 4)
		{
			byte b = array2[i + 3];
			if (b != 0)
			{
				for (int j = 0; j < 3; j++)
				{
					array[i + j] = (byte)((array2[i + j] * b + array[i + j] * (255 - b) + 127) / 255);
				}
				array[i + 3] = byte.MaxValue;
			}
		}
		BitmapSource bitmapSource = BitmapSource.Create(8, 8, 96.0, 96.0, PixelFormats.Bgra32, null, array, 32);
		bitmapSource.Freeze();
		return bitmapSource;
	}
}
