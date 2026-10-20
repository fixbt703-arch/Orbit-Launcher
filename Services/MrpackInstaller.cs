using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

/// <summary>Client-side MRPACK installation. All downloads are verified before touching the instance.</summary>
public sealed class MrpackInstaller
{
    private static readonly HttpClient Downloads = LauncherNetwork.CreateClient(redirects: false);
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private readonly HttpClient _downloads;
    public MrpackInstaller(HttpClient? downloads = null) => _downloads = downloads ?? Downloads;
    private const long MaxFile = 2L * 1024 * 1024 * 1024;
    public sealed record PackInfo(string Name, string Version, string Minecraft, string Loader, string LoaderVersion);
    private sealed record PackFile(string Path, long Size, string Sha1, string Sha512, string[] Urls);
    public sealed class Receipt
    {
        public PackInfo Info { get; set; } = new("", "", "", "", "");
        public string ArchiveHash { get; set; } = "";
        public List<string> Files { get; set; } = [];
    }

    public static PackInfo Inspect(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        using var index = ReadIndex(zip);
        return ReadInfo(index.RootElement);
    }

    public async Task<ModDownloadResult> InstallAsync(string archive, GameInstance instance, IProgress<double>? progress = null, CancellationToken token = default)
    {
        await InstallGate.WaitAsync(token);
        try { return await InstallCoreAsync(archive, instance, progress, token); }
        finally { InstallGate.Release(); }
    }

    private async Task<ModDownloadResult> InstallCoreAsync(string archive, GameInstance instance, IProgress<double>? progress, CancellationToken token)
    {
        var root = Path.GetFullPath(instance.DirectoryPath);
        EnsureNoLinks(root);
        using var zip = ZipFile.OpenRead(archive);
        using var index = ReadIndex(zip);
        var info = ReadInfo(index.RootElement);
        var files = ReadFiles(index.RootElement);
        var overrides = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in new[] { "overrides/", "client-overrides/" })
        {
            var layer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                if (entry.FullName.EndsWith('/')) continue;
                var path = ValidatePath(entry.FullName[prefix.Length..]);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Модпак содержит символическую ссылку.");
                if (!layer.Add(path)) throw new InvalidDataException($"Повторяющийся путь в модпаке: {path}");
                if (entry.Length > MaxFile) throw new InvalidDataException("Слишком большой файл в модпаке.");
                if (Path.GetFileName(path).Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                overrides[path] = entry;
            }
        }
        if (zip.Entries.Count > 20000 || files.Sum(f => f.Size) + overrides.Values.Sum(e => e.Length) > 10L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Модпак превышает допустимый размер.");
        var allPaths = files.Select(f => f.Path).Concat(overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var path in allPaths) EnsureNoLinks(Path.Combine(root, path));
        // A packed starting world must never replace or alter a world the player already has.
        var existingWorlds = allPaths.Where(p => p.StartsWith("saves/", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Split('/').ElementAtOrDefault(1) ?? "").Where(n => n.Length > 0 && Directory.Exists(Path.Combine(root, "saves", n)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        allPaths.RemoveAll(p => p.StartsWith("saves/", StringComparison.OrdinalIgnoreCase) && existingWorlds.Contains(p.Split('/')[1]));

        var job = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(root, ".orbit", "mrpack-staging", job);
        var backup = Path.Combine(root, ".orbit", "modpack-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + job);
        EnsureNoLinks(stage); EnsureNoLinks(backup); EnsureNoLinks(Path.Combine(root, "mods"));
        Directory.CreateDirectory(stage);
        try
        {
            int done = 0;
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (file, ct) =>
            {
                var target = Path.Combine(stage, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await DownloadAsync(file, target, ct);
                progress?.Report(5 + Interlocked.Increment(ref done) * 75d / Math.Max(1, files.Count));
            });
            foreach (var (path, entry) in overrides)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.Combine(stage, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = File.Create(target);
                await CopyBoundedAsync(input, output, entry.Length, token);
            }
            token.ThrowIfCancellationRequested();
            var receipt = new Receipt { Info = info, Files = allPaths.Order(StringComparer.OrdinalIgnoreCase).ToList() };
            await using (var input = File.OpenRead(archive)) receipt.ArchiveHash = Convert.ToHexString(await SHA512.HashDataAsync(input, token));
            var receiptPath = Path.Combine(root, ".orbit", "modpacks", receipt.ArchiveHash[..24] + ".json");
            EnsureNoLinks(receiptPath);
            // Commit without cancellation. Back up changed files and the previous mod set, then roll back on failure.
            Directory.CreateDirectory(backup);
            var written = new List<(string Destination, string? Backup)>();
            bool movedMods = false, createdMods = false;
            var oldGame = instance.GameVersion; var oldLoader = instance.Loader; var oldLoaderVersion = instance.LoaderVersion;
            var oldLaunch = instance.LaunchVersionId; var oldOrbit = instance.OrbitClientEnabled;
            void Put(string relative, string source)
            {
                var destination = Path.Combine(root, relative); EnsureNoLinks(destination);
                string? saved = null;
                if (File.Exists(destination)) { saved = Path.Combine(backup, "files", relative); Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(destination, saved, false); }
                written.Add((destination, saved));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            try
            {
                if (Directory.Exists(instance.ModsPath)) { Directory.Move(instance.ModsPath, Path.Combine(backup, "mods")); movedMods = true; }
                Directory.CreateDirectory(instance.ModsPath);
                createdMods = true;
                foreach (var path in allPaths) Put(path, Path.Combine(stage, path));
                instance.GameVersion = info.Minecraft; instance.Loader = info.Loader; instance.LoaderVersion = info.LoaderVersion;
                instance.LaunchVersionId = ""; instance.OrbitClientEnabled = false;
                var definition = Path.Combine(stage, "instance-definition.json");
                await File.WriteAllTextAsync(definition, JsonSerializer.Serialize(instance, JsonDefaults.Options));
                Put("instance.json", definition);
                var manifest = Path.Combine(stage, "installed-pack.json");
                await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(receipt, JsonDefaults.Options));
                Put(Path.GetRelativePath(root, receiptPath), manifest);
            }
            catch (Exception installError)
            {
                var failures = new List<Exception>();
                foreach (var item in written.AsEnumerable().Reverse())
                    try { if (item.Backup is null) File.Delete(item.Destination); else File.Copy(item.Backup, item.Destination, true); } catch (Exception e) { failures.Add(e); }
                try { if (createdMods && Directory.Exists(instance.ModsPath)) Directory.Delete(instance.ModsPath, true); if (movedMods) Directory.Move(Path.Combine(backup, "mods"), instance.ModsPath); } catch (Exception e) { failures.Add(e); }
                instance.GameVersion = oldGame; instance.Loader = oldLoader; instance.LoaderVersion = oldLoaderVersion; instance.LaunchVersionId = oldLaunch; instance.OrbitClientEnabled = oldOrbit;
                if (failures.Count > 0) throw new AggregateException($"Установка не завершена. Резервная копия: {backup}", new[] { installError }.Concat(failures));
                throw;
            }
            progress?.Report(100);
            return new ModDownloadResult(receiptPath, $"Модпак «{info.Name}» установлен в «{instance.Name}». Minecraft {info.Minecraft}, {info.Loader} {info.LoaderVersion}. Резервная копия: {backup}", true);
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (IOException e) { System.Diagnostics.Debug.WriteLine($"MRPACK staging cleanup: {e.Message}"); }
            catch (UnauthorizedAccessException e) { System.Diagnostics.Debug.WriteLine($"MRPACK staging cleanup: {e.Message}"); }
        }
    }

    private static JsonDocument ReadIndex(ZipArchive zip)
    {
        var entries = zip.Entries.Where(e => e.FullName == "modrinth.index.json").ToList();
        if (entries.Count != 1 || entries[0].Length > 8 * 1024 * 1024) throw new InvalidDataException("Не найден корректный modrinth.index.json.");
        using var input = entries[0].Open(); return JsonDocument.Parse(input);
    }
    private static PackInfo ReadInfo(JsonElement root)
    {
        if (root.GetProperty("formatVersion").GetInt32() != 1 || root.GetProperty("game").GetString() != "minecraft") throw new InvalidDataException("Неподдерживаемый формат модпака.");
        var dependencies = root.GetProperty("dependencies");
        string game = dependencies.GetProperty("minecraft").GetString() ?? "";
        var loaders = new Dictionary<string, string> { ["fabric-loader"] = "Fabric", ["quilt-loader"] = "Quilt", ["forge"] = "Forge", ["neoforge"] = "NeoForge" };
        var selected = dependencies.EnumerateObject().Where(p => loaders.ContainsKey(p.Name)).ToList();
        if (selected.Count > 1 || dependencies.EnumerateObject().Any(p => p.Name != "minecraft" && !loaders.ContainsKey(p.Name))) throw new InvalidDataException("Модпак требует неподдерживаемые компоненты.");
        var version = selected.Count == 0 ? "" : selected[0].Value.GetString() ?? "";
        if (!Regex.IsMatch(game, @"^[a-zA-Z0-9._+\-]+$") || selected.Count > 0 && !Regex.IsMatch(version, @"^[a-zA-Z0-9._+\-]+$")) throw new InvalidDataException("Некорректная версия игры или загрузчика.");
        return new PackInfo(root.GetProperty("name").GetString() ?? "Modpack", root.GetProperty("versionId").GetString() ?? "", game, selected.Count == 0 ? "Vanilla" : loaders[selected[0].Name], version);
    }
    private static List<PackFile> ReadFiles(JsonElement root)
    {
        var result = new List<PackFile>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in root.GetProperty("files").EnumerateArray())
        {
            var path = ValidatePath(item.GetProperty("path").GetString() ?? "");
            if (!seen.Add(path)) throw new InvalidDataException("Повторяющиеся пути файлов в модпаке.");
            if (item.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var client))
            {
                var side = client.GetString(); if (side == "unsupported") continue;
                if (side is not ("required" or "optional")) throw new InvalidDataException("Неизвестное окружение модпака.");
            }
            var hashes = item.GetProperty("hashes");
            var sha1 = hashes.GetProperty("sha1").GetString() ?? ""; var sha512 = hashes.GetProperty("sha512").GetString() ?? "";
            var size = item.GetProperty("fileSize").GetInt64();
            var urls = item.GetProperty("downloads").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
            if (size < 0 || size > MaxFile || !Regex.IsMatch(sha1, "^[a-fA-F0-9]{40}$") || !Regex.IsMatch(sha512, "^[a-fA-F0-9]{128}$") || urls.Length == 0) throw new InvalidDataException("Некорректные данные файла в модпаке.");
            foreach (var url in urls) ValidateUrl(new Uri(url, UriKind.Absolute));
            result.Add(new PackFile(path, size, sha1, sha512, urls));
        }
        return result;
    }
    internal static string ValidatePath(string path)
    {
        if (path.Length == 0 || path.Contains('\\') || path.StartsWith('/') || path.Contains(':')) throw new InvalidDataException("Небезопасный путь в модпаке.");
        var parts = path.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase))) throw new InvalidDataException("Небезопасный путь в модпаке.");
        if (new[] { ".orbit", "instance.json", "assets", "libraries", "runtime", "versions", "resources" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
            || new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".com", ".scr", ".msi", ".lnk", ".url", ".vbs" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Модпак содержит защищённый путь или исполняемый файл.");
        return path;
    }
    private static void EnsureNoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Установка модпака через ссылки на каталоги запрещена.");
    }
    private static void ValidateUrl(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !new[] { "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимый адрес загрузки в модпаке.");
    }
    private async Task DownloadAsync(PackFile file, string destination, CancellationToken token)
    {
        Exception? failure = null;
        foreach (var url in file.Urls) for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromMinutes(3));
                var downloadToken = deadline.Token;
                var uri = new Uri(url);
                for (int redirect = 0; redirect <= 5; redirect++)
                {
                    ValidateUrl(uri);
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.UserAgent.ParseAdd("OrbitLauncher/1.2");
                    using var response = await _downloads.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, downloadToken);
                    if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location) { uri = new Uri(uri, location); continue; }
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri is { } final) ValidateUrl(final);
                    await using (var input = await response.Content.ReadAsStreamAsync(downloadToken))
                    await using (var output = File.Create(destination)) await CopyBoundedAsync(input, output, file.Size, downloadToken);
                    await using var verified = File.OpenRead(destination);
                    if (!Convert.ToHexString(await SHA512.HashDataAsync(verified, downloadToken)).Equals(file.Sha512, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Не совпала SHA512: {file.Path}");
                    verified.Position = 0;
                    if (!Convert.ToHexString(await SHA1.HashDataAsync(verified, downloadToken)).Equals(file.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Не совпала SHA1: {file.Path}");
                    return;
                }
                throw new IOException("Слишком много перенаправлений.");
            }
            catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested) { failure = e; }
        }
        throw new IOException($"Не удалось скачать {file.Path}. Сборка не изменена.", failure);
    }
    private static async Task CopyBoundedAsync(Stream input, Stream output, long expected, CancellationToken token)
    {
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0) { total += read; if (total > expected) throw new InvalidDataException("Файл превышает объявленный размер."); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        if (total != expected) throw new InvalidDataException("Файл скачан не полностью.");
    }
    public static bool IsInstalled(GameInstance instance, string receiptPath)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receiptPath), JsonDefaults.Options);
            return receipt is not null && receipt.Info.Minecraft == instance.GameVersion && receipt.Info.Loader == instance.Loader
                && receipt.Files.All(p => { ValidatePath(p); var file = Path.Combine(instance.DirectoryPath, p); return File.Exists(file) || p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && File.Exists(file + ".disabled"); });
        }
        catch { return false; }
    }
}
