using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

/// <summary>Public GitHub Releases only. No account tokens or launcher credentials are sent.</summary>
public sealed class GitHubUpdateService(HttpClient httpClient, string? cacheRoot = null)
{
    public const string InstallerName = "OrbitLauncherInstaler.exe";
    public static Version CurrentVersion => typeof(GitHubUpdateService).Assembly.GetName().Version!;
    public static string DisplayVersion(Version version) => version.Revision > 0 ? version.ToString(4) : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
    private const long MaximumInstallerSize = 512L * 1024 * 1024;
    private static readonly string RepositoryPath = $"/{AppConfiguration.GitHubOwner}/{AppConfiguration.GitHubRepository}";
    private readonly string _cache = Path.GetFullPath(cacheRoot ?? Path.Combine(AppPaths.Cache, "updates"));
    private string? _publishedVersion;
    public string? PublishedVersion => _publishedVersion ??= ReadPublishedVersion();

    public static HttpClient CreateHttpClient()
    {
        var client = LauncherNetwork.CreateClient(redirects: false);
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var match = Regex.Match(value?.Trim() ?? "", @"^v?(\d+\.\d+(?:\.\d+){0,2})(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.IgnoreCase);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }

    public async Task<LauncherUpdate?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        try { return await CheckReleaseAsync(current, cancellationToken); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // The public page provides display metadata only; it cannot authorize an installer.
            try { await ReadPublishedPageAsync(cancellationToken); }
            catch (Exception pageError) when (!cancellationToken.IsCancellationRequested
                && pageError is HttpRequestException or OperationCanceledException or InvalidDataException) { }
            throw;
        }
    }

    private async Task<LauncherUpdate?> CheckReleaseAsync(Version current, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await SendAsync(new Uri($"https://api.github.com/repos{RepositoryPath}/releases/latest"), timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidDataException("Релизы GitHub пока недоступны. Нужен публичный репозиторий с опубликованным релизом.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub временно ограничил запросы. Попробуйте позже.");
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, timeout.Token);
        using var json = JsonDocument.Parse(bytes);
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var title = release.TryGetProperty("name", out var name) ? name.GetString() : null;
        if (!TryReadReleaseVersion(tag, title, out var version, out var display))
            throw new InvalidDataException("У релиза GitHub не указана версия. Используйте тег вида v1.4.1.");
        RememberPublishedVersion(display);
        var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(0, current.Build), Math.Max(0, current.Revision));
        if (version <= normalizedCurrent) return null;
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 200 || tag.Any(char.IsControl))
            throw new InvalidDataException("Некорректный тег релиза GitHub.");
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var installers = assets.Where(asset => asset.GetProperty("name").GetString() == InstallerName
            && asset.GetProperty("state").GetString() == "uploaded").ToArray();
        if (installers.Length != 1) throw new InvalidDataException("В релизе отсутствует установщик OrbitLauncherInstaler.exe.");
        var installer = installers[0];
        var size = installer.GetProperty("size").GetInt64();
        if (size < 2 || size > MaximumInstallerSize) throw new InvalidDataException("Некорректный размер установщика.");
        var download = AssetUri(installer, tag, InstallerName);
        var digest = installer.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() ?? "" : "";
        string hash;
        if (Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}$")) hash = digest[7..].ToLowerInvariant();
        else
        {
            var sums = assets.FirstOrDefault(asset => asset.GetProperty("name").GetString() == "SHA256SUMS.txt"
                && asset.GetProperty("state").GetString() == "uploaded");
            if (sums.ValueKind == JsonValueKind.Undefined)
                throw new InvalidDataException("У релиза нет SHA-256 установщика. Добавьте SHA256SUMS.txt.");
            using var sumsResponse = await SendAsync(AssetUri(sums, tag, "SHA256SUMS.txt"), timeout.Token);
            sumsResponse.EnsureSuccessStatusCode();
            hash = ReadChecksum(Encoding.UTF8.GetString(await ReadLimitedAsync(sumsResponse.Content, 65536, timeout.Token)));
        }
        var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        notes = new string(notes.Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t').ToArray());
        if (notes.Length > 12000) notes = notes[..12000] + "\n…";
        return new LauncherUpdate(version, tag, notes,
            new Uri($"https://github.com{RepositoryPath}/releases/tag/{Uri.EscapeDataString(tag)}"), download, size, hash);
    }

    private static bool TryReadReleaseVersion(string tag, string? title, out Version version, out string display)
    {
        var candidate = Regex.Replace(tag.Trim(), @"^Update[_ -]", "", RegexOptions.IgnoreCase);
        if (!TryParseVersion(candidate, out version))
        {
            var match = Regex.Match(title?.Trim() ?? "", @"^Orbit\s+Launcher\s*(v?\d+\.\d+(?:\.\d+){0,2}(?:\+[0-9A-Za-z.-]+)?)$", RegexOptions.IgnoreCase);
            candidate = match.Success ? match.Groups[1].Value : "";
            if (!TryParseVersion(candidate, out version)) { display = ""; return false; }
        }
        display = Regex.Match(candidate, @"\d+\.\d+(?:\.\d+){0,2}").Value;
        return true;
    }

    private string? ReadPublishedVersion()
    {
        try
        {
            var file = Path.Combine(_cache, "published-version.txt");
            if (!File.Exists(file) || new FileInfo(file).Length > 64) return null;
            using var stream = File.OpenRead(file);
            var bytes = new byte[65];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            var text = Encoding.UTF8.GetString(bytes, 0, count).Trim();
            return count <= 64 && Regex.IsMatch(text, @"^\d+\.\d+(?:\.\d+){0,2}$") && TryParseVersion(text, out _) ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void RememberPublishedVersion(string display)
    {
        _publishedVersion = display;
        try
        {
            Directory.CreateDirectory(_cache);
            File.WriteAllText(Path.Combine(_cache, "published-version.txt"), display);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task ReadPublishedPageAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await SendAsync(new Uri($"https://github.com{RepositoryPath}/releases/latest"), timeout.Token, releasePage: true);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri;
        var prefix = RepositoryPath + "/releases/tag/";
        if (finalUri is null || finalUri.Scheme != "https" || finalUri.Host != "github.com" || !finalUri.IsDefaultPort
            || finalUri.UserInfo.Length != 0 || finalUri.Query.Length != 0 || finalUri.Fragment.Length != 0
            || !finalUri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            || finalUri.AbsolutePath.Length == prefix.Length) throw new InvalidDataException("Не удалось определить релиз GitHub.");
        var html = new HtmlDocument();
        html.LoadHtml(Encoding.UTF8.GetString(await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, timeout.Token)));
        var title = WebUtility.HtmlDecode(html.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "");
        if (title.Length > 1000) throw new InvalidDataException("Некорректный заголовок релиза GitHub.");
        var match = Regex.Match(title, @"^Release\s+(.+?)\s+·\s+" + Regex.Escape(RepositoryPath[1..]) + @"\s+·\s+GitHub$", RegexOptions.IgnoreCase);
        var tag = Uri.UnescapeDataString(finalUri.AbsolutePath[prefix.Length..]);
        if (!match.Success || !TryReadReleaseVersion(tag, match.Groups[1].Value, out _, out var display))
            throw new InvalidDataException("У страницы релиза GitHub не указана версия.");
        RememberPublishedVersion(display);
    }

    public static string ReadChecksum(string text)
    {
        var hashes = text.TrimStart('\uFEFF').Split('\n').Select(line => Regex.Match(line.TrimEnd('\r'),
            @"^([0-9a-fA-F]{64})\s+\*?" + Regex.Escape(InstallerName) + @"\s*$"))
            .Where(match => match.Success).Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
        if (hashes.Length != 1) throw new InvalidDataException("Не удалось проверить SHA-256 установщика.");
        return hashes[0];
    }

    private static Uri AssetUri(JsonElement asset, string tag, string name)
    {
        var expected = new Uri($"https://github.com{RepositoryPath}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri)
            || uri != expected) throw new InvalidDataException("Ссылка на обновление не принадлежит релизу Orbit Launcher.");
        return uri;
    }

    public async Task<string> DownloadAsync(LauncherUpdate update, IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (update.Size < 2 || update.Size > MaximumInstallerSize || !Regex.IsMatch(update.Sha256, @"^[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("Некорректные данные обновления.");
        // Revalidate initial URL even when a caller supplies a release model directly.
        var expected = new Uri($"https://github.com{RepositoryPath}/releases/download/{Uri.EscapeDataString(update.Tag)}/{InstallerName}");
        if (update.DownloadUrl != expected) throw new InvalidDataException("Недопустимый источник обновления.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        Directory.CreateDirectory(_cache);
        var destination = Path.Combine(_cache, "installer-" + Guid.NewGuid().ToString("N") + ".exe");
        var temporary = destination + ".part";
        try
        {
            using var response = await SendAsync(update.DownloadUrl, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != update.Size)
                throw new InvalidDataException("Размер загрузки отличается от указанного в релизе.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536]; long received = 0; var nextReport = DateTime.MinValue;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, timeout.Token);
                    if (read == 0) break;
                    received += read;
                    if (received > update.Size) throw new InvalidDataException("Загруженный файл превышает ожидаемый размер.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    if (DateTime.UtcNow >= nextReport)
                    { progress?.Report(new(received, update.Size)); nextReport = DateTime.UtcNow.AddMilliseconds(80); }
                }
                if (received != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Проверка обновления не пройдена. Файл не будет установлен; повторите загрузку.");
                await output.FlushAsync(timeout.Token);
            }
            timeout.Token.ThrowIfCancellationRequested();
            // Refuse HTML or other payloads accidentally uploaded as an EXE.
            using (var file = File.OpenRead(temporary))
                if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("В релиз загружен некорректный EXE.");
            File.Move(temporary, destination);
            progress?.Report(new(update.Size, update.Size));
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token, bool releasePage = false)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                || !(uri.Host is "api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Недопустимый адрес сервера обновлений.");
            if (releasePage && (uri.Host != "github.com" || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || !(uri.AbsolutePath == RepositoryPath + "/releases/latest"
                    || uri.AbsolutePath.StartsWith(RepositoryPath + "/releases/tag/", StringComparison.Ordinal))))
                throw new InvalidDataException("Страница релиза не принадлежит Orbit Launcher.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("OrbitLauncher/" + DisplayVersion(CurrentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(releasePage ? "text/html" : uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream"));
            if (uri.Host == "api.github.com") request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.RequestMessage ??= request;
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidDataException("Пустое перенаправление GitHub.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new HttpRequestException("Слишком много перенаправлений GitHub.");
    }

    public void DiscardDownload(string file)
    {
        file = Path.GetFullPath(file);
        if (Path.GetDirectoryName(file) != _cache || !Regex.IsMatch(Path.GetFileName(file), @"^installer-[0-9a-f]{32}\.exe$")) return;
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximum) throw new InvalidDataException("Слишком большой ответ сервера обновлений.");
            output.Write(buffer, 0, read);
        }
    }
}
