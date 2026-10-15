using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace OrbitLauncher.Services;

public static class InstalledModMetadataService
{
    public static Task<(string Name, BitmapSource? Icon)> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => Read(path, cancellationToken), cancellationToken);

    private static (string Name, BitmapSource? Icon) Read(string path, CancellationToken token)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            token.ThrowIfCancellationRequested();
            var name = string.Empty;
            var iconPath = string.Empty;
            var fabric = Find(archive, "fabric.mod.json");
            var quilt = Find(archive, "quilt.mod.json");
            if (fabric is not null || quilt is not null)
            {
                using var stream = (fabric ?? quilt)!.Open();
                using var json = JsonDocument.Parse(stream);
                var root = json.RootElement;
                if (fabric is not null)
                {
                    name = Text(root, "name");
                    iconPath = IconPath(root, "icon");
                }
                else if (root.TryGetProperty("quilt_loader", out var loader))
                {
                    if (loader.TryGetProperty("metadata", out var metadata))
                    {
                        name = Text(metadata, "name");
                        iconPath = IconPath(metadata, "icon");
                    }
                }
            }
            else
            {
                var toml = Find(archive, "META-INF/mods.toml") ?? Find(archive, "META-INF/neoforge.mods.toml");
                if (toml is not null && toml.Length <= 2_000_000)
                {
                    using var reader = new StreamReader(toml.Open());
                    var content = reader.ReadToEnd();
                    name = Regex.Match(content, "(?m)^\\s*displayName\\s*=\\s*['\"]([^'\"]+)['\"]").Groups[1].Value;
                    iconPath = Regex.Match(content, "(?m)^\\s*logoFile\\s*=\\s*['\"]([^'\"]+)['\"]").Groups[1].Value;
                }
            }

            if (string.IsNullOrWhiteSpace(iconPath))
                iconPath = "pack.png";
            var icon = Find(archive, iconPath);
            if (icon is null || icon.Length is <= 0 or > 2_000_000)
                return (name, null);
            try
            {
                using var imageStream = new MemoryStream((int)icon.Length);
                using (var compressed = icon.Open())
                    compressed.CopyTo(imageStream);
                imageStream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 96;
                bitmap.StreamSource = imageStream;
                bitmap.EndInit();
                bitmap.Freeze();
                return (name, bitmap);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to decode mod icon in {path}: {ex}");
                return (name, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (string.Empty, null);
        }
    }

    private static ZipArchiveEntry? Find(ZipArchive archive, string path) =>
        archive.Entries.FirstOrDefault(item => item.FullName.Equals(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static string IconPath(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var icon))
            return string.Empty;
        if (icon.ValueKind == JsonValueKind.String)
            return icon.GetString() ?? string.Empty;
        if (icon.ValueKind == JsonValueKind.Object)
            return icon.EnumerateObject().OrderByDescending(item => int.TryParse(item.Name, out var size) ? size : 0)
                .Select(item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : string.Empty)
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
        return string.Empty;
    }
}
