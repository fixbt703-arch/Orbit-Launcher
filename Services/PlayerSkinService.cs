using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OrbitLauncher.Services;

public sealed class PlayerSkinService
{
    private readonly HttpClient _httpClient;

    public PlayerSkinService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<PlayerHeadImage?> GetHeadAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var nameUrl = $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(username)}";
        using var nameResponse = await _httpClient.GetAsync(nameUrl, cancellationToken);
        if (!nameResponse.IsSuccessStatusCode)
            return null;

        await using var nameStream = await nameResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var nameDocument = await JsonDocument.ParseAsync(nameStream, cancellationToken: cancellationToken);
        if (!nameDocument.RootElement.TryGetProperty("id", out var idElement))
            return null;

        var id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var profileUrl = $"https://sessionserver.mojang.com/session/minecraft/profile/{Uri.EscapeDataString(id)}";
        using var profileResponse = await _httpClient.GetAsync(profileUrl, cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
            return null;

        await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var profileDocument = await JsonDocument.ParseAsync(profileStream, cancellationToken: cancellationToken);
        if (!profileDocument.RootElement.TryGetProperty("properties", out var properties))
            return null;
        if (properties.ValueKind != JsonValueKind.Array)
            return null;

        string? encodedTextures = null;
        foreach (var property in properties.EnumerateArray())
        {
            if (!property.TryGetProperty("name", out var propertyName) || propertyName.GetString() != "textures"
                || !property.TryGetProperty("value", out var value))
                continue;
            encodedTextures = value.GetString();
            break;
        }
        if (string.IsNullOrWhiteSpace(encodedTextures))
            return null;

        var texturesJson = Encoding.UTF8.GetString(Convert.FromBase64String(encodedTextures));
        using var texturesDocument = JsonDocument.Parse(texturesJson);
        if (!texturesDocument.RootElement.TryGetProperty("textures", out var textures)
            || !textures.TryGetProperty("SKIN", out var skin)
            || !skin.TryGetProperty("url", out var skinUrlElement))
            return null;

        var skinUrl = skinUrlElement.GetString();
        if (string.IsNullOrWhiteSpace(skinUrl))
            return null;

        var bytes = await _httpClient.GetByteArrayAsync(skinUrl, cancellationToken);
        using var memory = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        bitmap.Freeze();
        if (bitmap.PixelWidth < 48 || bitmap.PixelHeight < 16)
            return null;

        var face = new CroppedBitmap(bitmap, new Int32Rect(8, 8, 8, 8));
        face.Freeze();
        var overlay = new CroppedBitmap(bitmap, new Int32Rect(40, 8, 8, 8));
        overlay.Freeze();
        return new PlayerHeadImage(face, overlay);
    }
}

public sealed record PlayerHeadImage(BitmapSource Face, BitmapSource Overlay);
