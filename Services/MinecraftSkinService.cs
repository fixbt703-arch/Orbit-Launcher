using System.Net.Http.Headers;
using OrbitLauncher.Utilities;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace OrbitLauncher.Services;

public sealed class MinecraftSkinService
{
    private const string ProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";
    private const string SkinsEndpoint = "https://api.minecraftservices.com/minecraft/profile/skins";
    private readonly HttpClient _httpClient;

    public MinecraftSkinService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<MinecraftSkinProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, ProfileEndpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return ParseProfile(payload);
    }

    public async Task<MinecraftSkinProfile> UploadAsync(
        string accessToken,
        byte[] pngBytes,
        string fileName,
        string variant,
        CancellationToken cancellationToken = default)
    {
        ValidateSkin(pngBytes);
        variant = NormalizeVariant(variant);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(variant), "variant");
        using var file = new ByteArrayContent(pngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "skin.png" : Path.GetFileName(fileName));

        using var request = CreateAuthorizedRequest(HttpMethod.Post, SkinsEndpoint, accessToken);
        request.Content = form;
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return ParseProfile(payload);
    }

    public async Task<byte[]> DownloadAsync(string textureUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(textureUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Minecraft вернул некорректный адрес скина.");
        if (uri.Scheme == Uri.UriSchemeHttp)
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        ValidateSkin(bytes);
        return bytes;
    }

    public static BitmapSource DecodeSkin(byte[] pngBytes)
    {
        ValidateSkin(pngBytes);
        using var memory = new MemoryStream(pngBytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static void ValidateSkin(byte[] pngBytes)
    {
        if (pngBytes.Length > 196608) throw new InvalidOperationException(LocalizationService.Translate("PNG должен быть меньше 192 КБ."));
        if (pngBytes.Length < 24 || pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47)
            throw new InvalidOperationException("Выберите изображение скина в формате PNG.");

        using var memory = new MemoryStream(pngBytes, writable: false);
        var decoder = new PngBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth != 64 || decoder.Frames[0].PixelHeight != 64)
            throw new InvalidOperationException("Скин должен иметь размер 64×64 пикселя.");
    }

    public static string NormalizeVariant(string? variant) =>
        string.Equals(variant, "slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Сессия Microsoft не содержит токен Minecraft.");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static MinecraftSkinProfile ParseProfile(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
        var name = root.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;

        MinecraftSkinInfo? active = null;
        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            foreach (var skin in skins.EnumerateArray())
            {
                var url = skin.TryGetProperty("url", out var urlNode) ? urlNode.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                var variant = skin.TryGetProperty("variant", out var variantNode) ? variantNode.GetString() : null;
                var state = skin.TryGetProperty("state", out var stateNode) ? stateNode.GetString() : null;
                var candidate = new MinecraftSkinInfo(url, NormalizeVariant(variant));
                active ??= candidate;
                if (string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    active = candidate;
                    break;
                }
            }
        }

        return new MinecraftSkinProfile(id, name, active);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("errorMessage", out var errorMessage))
                message = errorMessage.GetString() ?? string.Empty;
            else if (root.TryGetProperty("error", out var error))
                message = error.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // The HTTP status below remains useful when the service returns a non-JSON error page.
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(LocalizationService.Translate("Сессия Microsoft истекла. Выйдите из аккаунта и войдите снова."));
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException(LocalizationService.Translate("Аккаунт не имеет доступа к Minecraft: Java Edition или смене скина."));
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
            ? (LocalizationService.IsEnglish ? $"Minecraft Services returned error {(int)response.StatusCode}." : $"Minecraft Services вернул ошибку {(int)response.StatusCode}.")
            : message);
    }
}

public sealed record MinecraftSkinProfile(string Id, string Name, MinecraftSkinInfo? ActiveSkin);
public sealed record MinecraftSkinInfo(string Url, string Variant);
