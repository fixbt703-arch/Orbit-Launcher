using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class SkinLibraryService
{
    private readonly string _skins;
    private readonly string _index;
    public SkinLibraryService(string? skinsDirectory = null)
    {
        _skins = Path.GetFullPath(skinsDirectory ?? AppPaths.Skins);
        _index = Path.Combine(_skins, "library.json");
    }
    public async Task<IReadOnlyList<SkinLibraryItem>> LoadAsync()
    {
        Directory.CreateDirectory(_skins);
        if (!File.Exists(_index) && !File.Exists(_index + ".bak"))
            return [];

        var items = AtomicJsonFile.Read<List<SkinLibraryItem>>(_index) ?? [];
        var valid = new List<SkinLibraryItem>();
        foreach (var item in items)
        {
            try
            {
                if (item is null || string.IsNullOrWhiteSpace(item.FileName)) continue;
                item.FilePath = Path.Combine(_skins, Path.GetFileName(item.FileName));
                if (!File.Exists(item.FilePath))
                    continue;
                var bytes = await File.ReadAllBytesAsync(item.FilePath);
                var bitmap = MinecraftSkinService.DecodeSkin(bytes);
                item.Variant = MinecraftSkinService.NormalizeVariant(item.Variant);
                item.Preview = CreateFrontPreview(bitmap, item.Variant == "slim");
                valid.Add(item);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { }
        }
        return valid;
    }

    public async Task<SkinLibraryItem> ImportAsync(string sourcePath, string variant)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath);
        var bitmap = MinecraftSkinService.DecodeSkin(bytes);
        Directory.CreateDirectory(_skins);
        var id = Guid.NewGuid().ToString("N");
        var fileName = id + ".png";
        var destination = Path.Combine(_skins, fileName);
        variant = MinecraftSkinService.NormalizeVariant(variant);
        await File.WriteAllBytesAsync(destination, bytes);
        return new SkinLibraryItem
        {
            Id = id,
            Name = CleanName(Path.GetFileNameWithoutExtension(sourcePath)),
            Variant = MinecraftSkinService.NormalizeVariant(variant),
            FileName = fileName,
            FilePath = destination,
            Preview = CreateFrontPreview(bitmap, variant == "slim")
        };
    }

    public Task SaveAsync(IEnumerable<SkinLibraryItem> items) => AtomicJsonFile.SaveAsync(_index, items.ToList());

    public async Task DeleteAsync(SkinLibraryItem item, IEnumerable<SkinLibraryItem> remainingItems)
    {
        var path = Path.GetFullPath(item.FilePath);
        if (!string.Equals(Path.GetDirectoryName(path), _skins, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Удаление разрешено только для PNG внутри библиотеки скинов.");
        var fallback = remainingItems.ToList();
        await AtomicJsonFile.UpdateAsync<List<SkinLibraryItem>>(_index, saved =>
            (saved ?? fallback).Where(existing => existing.Id != item.Id && existing.FileName != item.FileName).ToList());
        if (File.Exists(item.FilePath))
            File.Delete(item.FilePath);
    }

    public static BitmapSource CreateFrontPreview(BitmapSource skin, bool slim)
    {
        BitmapSource source = skin;
        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            source = converted;
        }

        const int canvasWidth = 16;
        const int canvasHeight = 32;
        var pixels = new byte[canvasWidth * canvasHeight * 4];
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(8, 8, 8, 8), 4, 0);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(40, 8, 8, 8), 4, 0);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(20, 20, 8, 12), 4, 8);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(20, 36, 8, 12), 4, 8);

        var armWidth = slim ? 3 : 4;
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(44, 20, armWidth, 12), 4 - armWidth, 8);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(44, 36, armWidth, 12), 4 - armWidth, 8);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(36, 52, armWidth, 12), 12, 8);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(52, 52, armWidth, 12), 12, 8);

        DrawLayer(source, pixels, canvasWidth, new Int32Rect(4, 20, 4, 12), 4, 20);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(4, 36, 4, 12), 4, 20);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(20, 52, 4, 12), 8, 20);
        DrawLayer(source, pixels, canvasWidth, new Int32Rect(4, 52, 4, 12), 8, 20);

        const int scale = 5;
        var scaledWidth = canvasWidth * scale;
        var scaledHeight = canvasHeight * scale;
        var scaledStride = scaledWidth * 4;
        var scaled = new byte[scaledStride * scaledHeight];
        for (var y = 0; y < canvasHeight; y++)
        for (var x = 0; x < canvasWidth; x++)
        {
            var sourceIndex = (y * canvasWidth + x) * 4;
            for (var yy = 0; yy < scale; yy++)
            for (var xx = 0; xx < scale; xx++)
            {
                var destination = ((y * scale + yy) * scaledWidth + x * scale + xx) * 4;
                Buffer.BlockCopy(pixels, sourceIndex, scaled, destination, 4);
            }
        }
        var preview = BitmapSource.Create(scaledWidth, scaledHeight, 96, 96, PixelFormats.Bgra32, null, scaled, scaledStride);
        preview.Freeze();
        return preview;
    }

    private static void DrawLayer(BitmapSource source, byte[] canvas, int canvasWidth, Int32Rect sourceRect, int destinationX, int destinationY)
    {
        var stride = sourceRect.Width * 4;
        var layer = new byte[stride * sourceRect.Height];
        source.CopyPixels(sourceRect, layer, stride, 0);
        for (var y = 0; y < sourceRect.Height; y++)
        for (var x = 0; x < sourceRect.Width; x++)
        {
            var sourceIndex = y * stride + x * 4;
            var alpha = layer[sourceIndex + 3];
            if (alpha == 0)
                continue;
            var destination = ((destinationY + y) * canvasWidth + destinationX + x) * 4;
            if (alpha == 255)
            {
                Buffer.BlockCopy(layer, sourceIndex, canvas, destination, 4);
                continue;
            }
            var inverse = 255 - alpha;
            canvas[destination] = (byte)((layer[sourceIndex] * alpha + canvas[destination] * inverse) / 255);
            canvas[destination + 1] = (byte)((layer[sourceIndex + 1] * alpha + canvas[destination + 1] * inverse) / 255);
            canvas[destination + 2] = (byte)((layer[sourceIndex + 2] * alpha + canvas[destination + 2] * inverse) / 255);
            canvas[destination + 3] = (byte)Math.Min(255, alpha + canvas[destination + 3] * inverse / 255);
        }
    }

    private static string CleanName(string name)
    {
        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? "Skin" : name.Length > 28 ? name[..28] : name;
    }
}
