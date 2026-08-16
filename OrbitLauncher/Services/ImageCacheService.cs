using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using OrbitLauncher.Utilities;
using SkiaSharp;

namespace OrbitLauncher.Services;

public sealed class ImageCacheService
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly string _cacheDirectory = Path.Combine(AppPaths.Cache, "images");

    public ImageCacheService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<BitmapSource?> GetAsync(string url, int decodePixelWidth, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return null;

        var cachePath = Path.Combine(_cacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".image");
        var convertedPath = cachePath + ".png";
        var gate = _locks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length == 0)
                await DownloadAsync(url, cachePath, cancellationToken);
        }
        catch
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
            return null;
        }
        finally
        {
            gate.Release();
        }

        try
        {
            return await Task.Run(
                () => File.Exists(convertedPath)
                    ? LoadWpfBitmap(convertedPath, decodePixelWidth)
                    : LoadBitmap(cachePath, convertedPath, decodePixelWidth),
                cancellationToken);
        }
        catch
        {
            try
            {
                File.Delete(cachePath);
                File.Delete(convertedPath);
            }
            catch
            {
                // Повторная загрузка произойдёт при следующем запросе.
            }
            return null;
        }
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp";
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var output = File.Create(temporary))
                        await input.CopyToAsync(output, cancellationToken);
                    File.Move(temporary, destination, true);
                    return;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
                catch (HttpRequestException) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
                catch (IOException) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
            }
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }
    }

    private static BitmapSource LoadBitmap(string path, string convertedPath, int decodePixelWidth)
    {
        try
        {
            return LoadWpfBitmap(path, decodePixelWidth);
        }
        catch
        {
            return LoadSkiaBitmap(path, convertedPath, decodePixelWidth);
        }
    }

    private static BitmapSource LoadWpfBitmap(string path, int decodePixelWidth)
    {
        using var stream = File.OpenRead(path);
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

    private static BitmapSource LoadSkiaBitmap(string path, string convertedPath, int decodePixelWidth)
    {
        using var input = File.OpenRead(path);
        using var source = SKBitmap.Decode(input) ?? throw new InvalidDataException("Формат изображения не поддерживается.");
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("Не удалось преобразовать изображение.");
        var pngBytes = encoded.ToArray();
        var temporary = convertedPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, pngBytes);
            File.Move(temporary, convertedPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        using var png = new MemoryStream(pngBytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
            bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.StreamSource = png;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
