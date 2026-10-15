using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitLauncher.Utilities;
using SkiaSharp;

namespace OrbitLauncher.Services;

/// <summary>
/// Loads catalog images without writing them to disk. Network and decoding work
/// is deliberately bounded so a page with broken or unusually large images
/// cannot saturate the processor, memory or connection pool.
/// </summary>
public sealed class ImageCacheService
{
    private const int MaximumCacheEntries = 128;
    private static readonly TimeSpan ImageRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _memoryCache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _cacheOrder = new();
    private readonly SemaphoreSlim _networkLimiter = new(4, 4);
    private readonly SemaphoreSlim _decodeLimiter = new(2, 2);

    public ImageCacheService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        RemoveLegacyDiskCache();
    }

    public async Task<BitmapSource?> GetAsync(string url, int decodePixelWidth, CancellationToken cancellationToken = default)
    {
        if (!IsSupportedWebUrl(url))
            return null;

        var key = $"{decodePixelWidth}|{url}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var candidate = new Lazy<Task<BitmapSource?>>(
                () => DownloadAndDecodeAsync(url, decodePixelWidth, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var lazy = _memoryCache.GetOrAdd(key, candidate);
            if (ReferenceEquals(lazy, candidate))
            {
                _cacheOrder.Enqueue(key);
                TrimMemoryCache();
            }

            try
            {
                var image = await lazy.Value.WaitAsync(cancellationToken);
                if (image is null)
                    RemoveIfCurrent(key, lazy);
                return image;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Another search owned and cancelled this shared request.
                // A still-active caller gets one fresh attempt.
                RemoveIfCurrent(key, lazy);
            }
            catch (OperationCanceledException)
            {
                if (lazy.IsValueCreated && lazy.Value.IsCanceled)
                    RemoveIfCurrent(key, lazy);
                throw;
            }
            catch
            {
                // A malformed image must not be decoded repeatedly during one view.
                return null;
            }
        }
        return null;
    }

    private void RemoveIfCurrent(string key, Lazy<Task<BitmapSource?>> lazy)
    {
        if (_memoryCache.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
            _memoryCache.TryRemove(key, out _);
    }

    public async Task<byte[]?> GetBytesAsync(
        string url,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedWebUrl(url) || maximumBytes <= 0)
            return null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await _networkLimiter.WaitAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ImageRequestTimeout);
                using var response = await _httpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode < 500
                        && response.StatusCode is not HttpStatusCode.RequestTimeout
                        && (int)response.StatusCode != 429)
                    {
                        return null;
                    }
                    if (attempt == 2)
                        return null;
                    continue;
                }

                if (response.Content.Headers.ContentLength is > 0
                    && response.Content.Headers.ContentLength > maximumBytes)
                {
                    return null;
                }

                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream(
                    response.Content.Headers.ContentLength is > 0
                        ? (int)Math.Min(response.Content.Headers.ContentLength.Value, maximumBytes)
                        : Math.Min(maximumBytes, 64 * 1024));
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, timeout.Token);
                    if (read == 0)
                        break;
                    if (output.Length + read > maximumBytes)
                        return null;
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                return output.Length == 0 ? null : output.ToArray();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 2)
                    return null;
            }
            catch (HttpRequestException)
            {
                if (attempt == 2)
                    return null;
            }
            catch (IOException)
            {
                if (attempt == 2)
                    return null;
            }
            finally
            {
                _networkLimiter.Release();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }

        return null;
    }

    private async Task<BitmapSource?> DownloadAndDecodeAsync(
        string url,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        var maximumBytes = decodePixelWidth is > 0 and <= 128 ? 5_000_000 : 20_000_000;
        var bytes = await GetBytesAsync(url, maximumBytes, cancellationToken);
        if (bytes is null)
            return null;

        await _decodeLimiter.WaitAsync(cancellationToken);
        try
        {
            try
            {
                return await Task.Run(() => Decode(bytes, decodePixelWidth), cancellationToken);
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            _decodeLimiter.Release();
        }
    }

    private static BitmapSource Decode(byte[] bytes, int decodePixelWidth)
    {
        try
        {
            return LoadWpfBitmap(bytes, decodePixelWidth);
        }
        catch
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported image format.");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
                throw new InvalidDataException("Invalid image dimensions.");

            var scale = decodePixelWidth > 0 && codec.Info.Width > decodePixelWidth
                ? decodePixelWidth / (float)codec.Info.Width
                : 1f;
            var dimensions = codec.GetScaledDimensions(scale);
            var imageInfo = new SKImageInfo(
                Math.Max(1, dimensions.Width),
                Math.Max(1, dimensions.Height),
                SKColorType.Bgra8888,
                SKAlphaType.Premul);
            if ((long)imageInfo.Width * imageInfo.Height > 20_000_000)
                throw new InvalidDataException("Image dimensions are too large.");

            using var bitmap = new SKBitmap(imageInfo);
            var result = codec.GetPixels(imageInfo, bitmap.GetPixels(), SKCodecOptions.Default);
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                throw new InvalidDataException($"Image could not be decoded: {result}.");
            var pixels = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            var image = BitmapSource.Create(imageInfo.Width, imageInfo.Height, 96, 96,
                PixelFormats.Pbgra32, null, pixels, bitmap.RowBytes);
            image.Freeze();
            return image;
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

    private void TrimMemoryCache()
    {
        while (_memoryCache.Count > MaximumCacheEntries && _cacheOrder.TryDequeue(out var oldestKey))
            _memoryCache.TryRemove(oldestKey, out _);
    }

    public void Clear()
    {
        _memoryCache.Clear();
        while (_cacheOrder.TryDequeue(out _)) { }
    }

    private static bool IsSupportedWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

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
