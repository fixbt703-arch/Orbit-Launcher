using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Net.Http;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services.Connect;

public sealed record ConnectRuntimePaths(string Core, string Cli, string Terracotta);

public sealed class ConnectRuntimeInstaller
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;
    private static readonly Dictionary<string, string> EasyTierFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["easytier-core.exe"] = "DA7EB2D24B5416F3D3407636949E964A0750E3F9DC53A828CB6799A57EAD445D",
        ["easytier-cli.exe"] = "D8783E851E944B44A9B71B39FD02F227EC0A2A82B3165C55EAD5DD32DCDE53A1",
        ["Packet.dll"] = "C7C03A87EAC7243CCBE331554624B18803010B740E311FC8CFDDB573096EACAC"
    };
    private static readonly Dictionary<string, string> TerracottaFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["terracotta-0.4.2-windows-x86_64.exe"] = "74C10568A7FEA9C1D38CF8D2D4CA90BAF1517F8E5A26C63D3349DB70BC449796",
        ["VCRUNTIME140.DLL"] = "475AB98B7722E965BD38C8FA6ED23502309582CCF294FF1061CB290C7988F0D1"
    };

    public ConnectRuntimeInstaller(string? root = null) => _root = Path.GetFullPath(root ?? Path.Combine(AppPaths.Root, "connect", "runtime"));

    public async Task<ConnectRuntimePaths> EnsureAsync(CancellationToken token = default)
    {
        if (!Environment.Is64BitProcess || !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Orbit Connect требует Windows x64.");
        var gate = Gates.GetOrAdd(_root, _ => new(1, 1)); await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(_root);
            await using var installLock = await AcquireInstallLockAsync(token);
            string et = Path.Combine(_root, "easytier-2.6.4"), tc = Path.Combine(_root, "terracotta-0.4.2");
            if (!await ValidAsync(et, EasyTierFiles, token))
            {
                using var archive = await DownloadAsync("https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-x86_64-v2.6.4.zip", token);
                await UnpackZipAsync(archive, et, "27AF91E270E554709B048BD32327FEFD2DFCE5062AE1E8701AF7550C6F525F84", EasyTierFiles, token);
            }
            if (!await ValidAsync(tc, TerracottaFiles, token))
            {
                using var archive = await DownloadAsync("https://github.com/burningtnt/Terracotta/releases/download/v0.4.2/terracotta-0.4.2-windows-x86_64-pkg.tar.gz", token);
                using var checkedBytes = await CheckedAsync(archive, "07EBE139E3CA5F74576E58B1A96EFE59ABDFBE148D3F1A49BFDCA8B6F70745F0", token);
                using var gzip = new GZipStream(checkedBytes, CompressionMode.Decompress);
                using var tar = new TarReader(gzip);
                var entries = new List<(string Name, byte[] Bytes)>(); int total = 0;
                while (await tar.GetNextEntryAsync(copyData: false, cancellationToken: token) is { } entry)
                {
                    CheckArchivePath(entry.Name);
                    if (entry.EntryType is TarEntryType.Directory) continue;
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("Недопустимая запись компонента.");
                    string name = Path.GetFileName(entry.Name);
                    if (!TerracottaFiles.ContainsKey(name)) continue;
                    var data = await ReadBoundedAsync(entry.DataStream!, 48 * 1024 * 1024 - total, token);
                    total += data.Length; entries.Add((name, data));
                }
                await CommitAsync(tc, entries, TerracottaFiles, token);
            }
            string legal = Path.Combine(_root, "licenses"); Directory.CreateDirectory(legal);
            foreach (string name in typeof(ConnectRuntimeInstaller).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("OrbitLauncher.Connect.Legal.", StringComparison.Ordinal)))
            {
                using var text = typeof(ConnectRuntimeInstaller).Assembly.GetManifestResourceStream(name)!;
                using var target = File.Create(Path.Combine(legal, name["OrbitLauncher.Connect.Legal.".Length..])); await text.CopyToAsync(target, token);
            }
            return new(Path.Combine(et, "easytier-core.exe"), Path.Combine(et, "easytier-cli.exe"), Path.Combine(tc, "terracotta-0.4.2-windows-x86_64.exe"));
        }
        finally { gate.Release(); }
    }

    private async Task<FileStream> AcquireInstallLockAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, deadline.Token); }
        }
    }
    private static async Task<Stream> DownloadAsync(string address, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        using var http = LauncherNetwork.CreateClient(redirects: false);
        var uri = new Uri(address);
        for (int redirects = 0; redirects < 6; redirects++)
        {
            if (uri.Scheme != "https" || !(uri.Host == "github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Недопустимый источник сетевого компонента.");
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            { uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue; }
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            return new MemoryStream(await ReadBoundedAsync(input, 64 * 1024 * 1024, deadline.Token), writable: false);
        }
        throw new IOException("Слишком много перенаправлений при загрузке компонента.");
    }

    public static async Task UnpackZipAsync(Stream archive, string destination, string archiveHash,
        IReadOnlyDictionary<string, string> allowed, CancellationToken token)
    {
        using var bytes = await CheckedAsync(archive, archiveHash, token);
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        var files = new List<(string Name, byte[] Bytes)>(); int total = 0;
        foreach (var entry in zip.Entries)
        {
            CheckArchivePath(entry.FullName);
            if (entry.Name.Length == 0 || !allowed.ContainsKey(entry.Name)) continue;
            using var input = entry.Open();
            var data = await ReadBoundedAsync(input, 64 * 1024 * 1024 - total, token);
            total += data.Length; files.Add((entry.Name, data));
        }
        await CommitAsync(destination, files, allowed, token);
    }

    private static void CheckArchivePath(string path)
    {
        if (path.Length > 512 || path.Contains(':') || path.StartsWith('/') || path.StartsWith('\\')
            || path.Replace('\\', '/').Split('/').Any(s => s == "..")) throw new InvalidDataException("Недопустимый путь в компоненте.");
    }

    private static async Task<MemoryStream> CheckedAsync(Stream archive, string hash, CancellationToken token)
    {
        byte[] bytes = await ReadBoundedAsync(archive, 64 * 1024 * 1024, token);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Проверка целостности компонента Orbit Connect не пройдена.");
        return new(bytes, writable: false);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, int limit, CancellationToken token)
    {
        using var output = new MemoryStream(); byte[] buffer = new byte[32768];
        while (true) { int count = await input.ReadAsync(buffer, token); if (count == 0) return output.ToArray();
            if (count > limit - output.Length) throw new InvalidDataException("Компонент превышает допустимый размер.");
            output.Write(buffer, 0, count); }
    }

    private static async Task CommitAsync(string destination, List<(string Name, byte[] Bytes)> files,
        IReadOnlyDictionary<string, string> allowed, CancellationToken token)
    {
        if (files.Count != allowed.Count || files.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allowed.Count)
            throw new InvalidDataException("Неполный или неоднозначный компонент Orbit Connect.");
        foreach (var file in files)
            if (Path.GetFileName(file.Name) != file.Name || !Convert.ToHexString(SHA256.HashData(file.Bytes)).Equals(allowed[file.Name], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Файл компонента Orbit Connect повреждён.");
        token.ThrowIfCancellationRequested(); Directory.CreateDirectory(destination);
        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("Каталог компонента не должен быть ссылкой.");
        foreach (var file in files)
        {
            string target = Path.Combine(destination, file.Name), temp = target + "." + Guid.NewGuid().ToString("N") + ".part";
            try { await File.WriteAllBytesAsync(temp, file.Bytes, token); File.Move(temp, target, overwrite: true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    private static async Task<bool> ValidAsync(string directory, IReadOnlyDictionary<string, string> files, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return false;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return false;
        foreach (var file in files)
        {
            string path = Path.Combine(directory, file.Key);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            using var input = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(file.Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
