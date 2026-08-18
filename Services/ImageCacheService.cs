using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using OrbitLauncher.Utilities;
using SkiaSharp;

namespace OrbitLauncher.Services;

/// <summary>
/// Loads catalog images without writing them to disk. The small in-memory cache
/// belongs to one catalog window and disappears as soon as that window is closed.
/// </summary>
public sealed class ImageCacheService
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _memoryCache = new(StringComparer.Ordinal);

    public ImageCacheService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        RemoveLegacyDiskCache();
    }

    public async Task<BitmapSource?> GetAsync(string url, int decodePixelWidth, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return null;

        var key = $"{decodePixelWidth}|{url}";
        var lazy = _memoryCache.GetOrAdd(key, _ => new Lazy<Task<BitmapSource?>>(
            () => DownloadAndDecodeAsync(url, decodePixelWidth, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _memoryCache.TryRemove(key, out _);
            throw;
        }
        catch
        {
            _memoryCache.TryRemove(key, out _);
            return null;
        }
    }

    private async Task<BitmapSource?> DownloadAndDecodeAsync(string url, int decodePixelWidth, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0)
                    return null;
                return await Task.Run(() => Decode(bytes, decodePixelWidth), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        return null;
    }

    private static BitmapSource Decode(byte[] bytes, int decodePixelWidth)
    {
        try
        {
            return LoadWpfBitmap(bytes, decodePixelWidth);
        }
        catch
        {
            using var source = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Unsupported image format.");
            using var image = SKImage.FromBitmap(source);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("Image conversion failed.");
            return LoadWpfBitmap(encoded.ToArray(), decodePixelWidth);
        }
    }

    private static BitmapSource LoadWpfBitmap(byte[] bytes, int decodePixelWidth)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
            image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void RemoveLegacyDiskCache()
    {
        var directory = Path.Combine(AppPaths.Cache, "images");
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch
        {
            // A locked legacy cache is harmless; no new files are written there.
        }
    }
}
