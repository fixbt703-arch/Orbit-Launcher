using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class ElySkinService
{
    private readonly HttpClient _http;
    private readonly PlayerSkinService _heads;
    private readonly ConcurrentDictionary<string, RefreshState> _refreshes = new(StringComparer.OrdinalIgnoreCase);
    private sealed class RefreshState { public long Revision; }
    public ElySkinService(HttpClient http, string? cache = null)
    {
        _http = http;
        _heads = new PlayerSkinService(http, cache ?? Path.Combine(AppPaths.Cache, "ely-heads"));
    }
    public PlayerHeadImage? GetCachedHead(string name) => _heads.GetCachedHead(name);

    public async Task<PlayerHeadImage?> RefreshHeadAsync(string name, CancellationToken token = default)
    {
        var refresh = _refreshes.GetOrAdd(name, _ => new RefreshState());
        long revision;
        lock (refresh) revision = ++refresh.Revision;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await DownloadAsync(new Uri("https://skinsystem.ely.by/skins/" + Uri.EscapeDataString(name) + ".png?version=2"), deadline.Token);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            lock (refresh)
            {
                if (revision == refresh.Revision) _heads.ForgetSkin(name);
                return _heads.GetCachedHead(name);
            }
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
        {
            if (buffer.Length + read > 196608) throw new InvalidDataException("Ely.by вернул слишком большой файл скина.");
            buffer.Write(chunk, 0, read);
        }
        lock (refresh)
        {
            if (revision != refresh.Revision) return _heads.GetCachedHead(name);
            buffer.Position = 0;
            var image = new PngBitmapDecoder(buffer, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (image.Frames.Count == 0 || image.Frames[0].PixelWidth != 64 || image.Frames[0].PixelHeight is not (32 or 64))
                throw new InvalidDataException("Ely.by вернул некорректный размер скина.");
            _heads.RememberSkin(name, buffer.ToArray());
            return _heads.GetCachedHead(name);
        }
    }

    private async Task<HttpResponseMessage> DownloadAsync(Uri address, CancellationToken token)
    {
        for (int hop = 0; hop < 5; hop++)
        {
            var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidDataException("Ely.by не вернул адрес текстуры.");
            var next = location.IsAbsoluteUri ? location : new Uri(address, location);
            // Ely.by's skin endpoint still returns some HTTP storage URLs. Upgrade before requesting.
            if (!next.IsDefaultPort || !string.IsNullOrEmpty(next.UserInfo) ||
                (next.Host != "ely.by" && next.Host != "skinsystem.ely.by") || (next.Scheme != "https" && next.Scheme != "http"))
                throw new InvalidDataException("Некорректный адрес текстуры Ely.by.");
            address = new UriBuilder(next) { Scheme = "https", Port = -1 }.Uri;
        }
        throw new InvalidDataException("Слишком много перенаправлений Ely.by.");
    }
}
