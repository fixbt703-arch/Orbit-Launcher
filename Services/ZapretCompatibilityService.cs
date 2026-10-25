using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed record ZapretCompatibilityResult(bool Changed, string? BackupPath);

public sealed class ZapretCompatibilityOwnershipException : InvalidOperationException
{
    public ZapretCompatibilityOwnershipException() : base("OrbitLauncher cannot safely identify its Zapret exclusions. The list was left unchanged.") { }
}

public static class ZapretCompatibilityService
{
    private const int MaximumListBytes = 1024 * 1024;
    private const string BeginMarker = "# OrbitLauncher Zapret compatibility BEGIN ";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex LegacyName = new(@"\Alist-exclude-user-[0-9]{8}T[0-9]{9}-[0-9a-fA-F]{32}\.txt\z", RegexOptions.CultureInvariant);
    private static readonly Regex OriginalName = new(@"\Aoriginal-([0-9a-f]{32})\.txt\z", RegexOptions.CultureInvariant);
    public static IReadOnlyList<string> Domains { get; } = Array.AsReadOnly(new[]
    {
        "mojang.com", "minecraft.net", "minecraftservices.com", "fabricmc.net", "quiltmc.org",
        "minecraftforge.net", "neoforged.net", "modrinth.com", "curseforge.com", "forgecdn.net",
        "ely.by", "github.com", "githubusercontent.com", "githubassets.com", "microsoftonline.com",
        "microsoft.com", "live.com", "xboxlive.com", "msauth.net", "msftauth.net"
    });

    public static Task<ZapretCompatibilityResult> ApplyAsync(string zapretRoot, CancellationToken token = default, string? backupRoot = null) =>
        ChangeAsync(zapretRoot, token, backupRoot, remove: false);

    public static Task<ZapretCompatibilityResult> RemoveAsync(string zapretRoot, CancellationToken token = default, string? backupRoot = null) =>
        ChangeAsync(zapretRoot, token, backupRoot, remove: true);

    public static async Task<bool> HasCompatibilityAsync(string zapretRoot, CancellationToken token = default, string? backupRoot = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(zapretRoot));
        var backups = BackupFolder(root, backupRoot);
        var target = Path.Combine(root, "lists", "list-exclude-user.txt");
        ValidatePaths(root, Path.Combine(root, "lists"), target, backups);
        var current = await ReadFileAsync(target, token).ConfigureAwait(false);
        var restored = await RemoveOwnedAsync(current, backups, new List<string>(), token,
            inspectOnly: true, legacy: Path.Combine(root, ".orbit-launcher-backups")).ConfigureAwait(false);
        return !current.AsSpan().SequenceEqual(restored);
    }

    private static string BackupFolder(string root, string? backupRoot)
    {
        var hash = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(root.ToUpperInvariant()))).ToLowerInvariant();
        var backups = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupRoot ?? Path.Combine(AppPaths.Root, "backups", "zapret", hash)));
        if (IsWithin(backups, root)) throw new InvalidDataException("Zapret backups must be outside the selected Zapret folder.");
        return backups;
    }

    private static async Task<ZapretCompatibilityResult> ChangeAsync(string zapretRoot, CancellationToken token, string? backupRoot, bool remove)
    {
        token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(zapretRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(zapretRoot));
        var backups = BackupFolder(root, backupRoot);
        var gate = Gates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var lists = Path.Combine(root, "lists");
            var target = Path.Combine(lists, "list-exclude-user.txt");
            ValidatePaths(root, lists, target, backups);
            // Deny in-place writers while parsing and replacing the selected list.
            await using var input = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous);
            var original = await ReadAsync(input, token).ConfigureAwait(false);
            await ImportLegacyAsync(root, lists, target, backups, token).ConfigureAwait(false);
            byte[] replacement;
            string? backup;
            var legacyCompletions = new List<string>();
            if (remove)
            {
                replacement = await RemoveOwnedAsync(original, backups, legacyCompletions, token).ConfigureAwait(false);
                if (replacement.AsSpan().SequenceEqual(original))
                {
                    await CompleteLegacyAsync(backups, legacyCompletions, token).ConfigureAwait(false);
                    return new(false, null);
                }
                // Keep the pre-removal version too, so every successful write has a durable backup.
                backup = Path.Combine(backups, "before-remove-" + Guid.NewGuid().ToString("N") + ".txt");
                await SaveAsync(backups, backup, original, token).ConfigureAwait(false);
            }
            else
            {
                var id = Guid.NewGuid().ToString("N");
                replacement = Append(original, id);
                if (replacement.AsSpan().SequenceEqual(original)) return new(false, null);
                backup = Path.Combine(backups, "original-" + id + ".txt");
                // Both immutable versions precede the list write. A crash leaves usable proof or harmless unused backups.
                await SaveAsync(backups, backup, original, token).ConfigureAwait(false);
                await SaveAsync(backups, Path.Combine(backups, "applied-" + id + ".txt"), replacement, token).ConfigureAwait(false);
            }
            var temporary = Path.Combine(lists, ".orbit-launcher-pending-" + Guid.NewGuid().ToString("N"));
            try
            {
                ValidatePaths(root, lists, target, backups);
                await WriteNewAsync(temporary, replacement, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                ValidatePaths(root, lists, target, backups);
                await EnsureUnchangedAsync(target, original, token).ConfigureAwait(false);
                // The replacement stays on the list's volume; backups may be on a different drive.
                File.Replace(temporary, target, null, ignoreMetadataErrors: true);
                await CompleteLegacyAsync(backups, legacyCompletions, CancellationToken.None).ConfigureAwait(false);
                return new(true, backup);
            }
            finally
            {
                if (File.Exists(temporary)) { RequireFile(temporary); File.Delete(temporary); }
            }
        }
        finally { gate.Release(); }
    }

    private static byte[] Append(byte[] original, string? id)
    {
        var text = StrictUtf8.GetString(original);
        var existing = DomainLines(text);
        var missing = Domains.Where(domain => !existing.Contains(domain)).ToArray();
        if (missing.Length == 0) return original;
        var newline = Newline(text);
        var separator = NeedsSeparator(original) ? newline : "";
        var block = string.Join(newline, missing) + newline;
        if (id != null) block = BeginMarker + id + newline + block + "# OrbitLauncher Zapret compatibility END " + id + newline;
        var appended = StrictUtf8.GetBytes(separator + block);
        if (original.Length + appended.Length > MaximumListBytes) throw new InvalidDataException("Zapret exclusion list exceeds 1 MiB.");
        return original.Concat(appended).ToArray();
    }

    private static async Task<byte[]> RemoveOwnedAsync(byte[] current, string backups, List<string> legacyCompletions, CancellationToken token, bool inspectOnly = false, string? legacy = null)
    {
        var result = current;
        if (Directory.Exists(backups))
        {
            foreach (var path in Directory.EnumerateFiles(backups).Where(path => OriginalName.IsMatch(Path.GetFileName(path))).OrderBy(path => path, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var id = OriginalName.Match(Path.GetFileName(path)).Groups[1].Value;
                var appliedPath = Path.Combine(backups, "applied-" + id + ".txt");
                if (!Path.Exists(appliedPath)) continue;
                var original = await ReadFileAsync(path, token).ConfigureAwait(false);
                var applied = await ReadFileAsync(appliedPath, token).ConfigureAwait(false);
                if (!Append(original, id).AsSpan().SequenceEqual(applied)) throw new ZapretCompatibilityOwnershipException();
                if (result.AsSpan().SequenceEqual(applied)) { result = original; continue; }
                var text = StrictUtf8.GetString(result);
                var appliedText = StrictUtf8.GetString(applied);
                var offset = appliedText.IndexOf(BeginMarker + id, StringComparison.Ordinal);
                if (offset < 0) continue;
                var block = appliedText[offset..];
                var start = text.IndexOf(block, StringComparison.Ordinal);
                if (start < 0)
                {
                    if (text.Contains(BeginMarker + id, StringComparison.Ordinal)) throw new ZapretCompatibilityOwnershipException();
                    continue;
                }
                if (text.IndexOf(block, start + block.Length, StringComparison.Ordinal) >= 0) throw new ZapretCompatibilityOwnershipException();
                result = StrictUtf8.GetBytes(text.Remove(start, block.Length));
            }
        }
        result = RemoveManagedComments(result);
        if (Directory.Exists(backups) || legacy != null && Directory.Exists(legacy))
        {
            // Old versions appended unmarked domains. Only the unchanged original prefix proves ownership of that range.
            var candidates = new List<byte[]>();
            foreach (var path in new[] { backups, legacy }.Where(directory => directory != null && Directory.Exists(directory))
                .SelectMany(directory => Directory.EnumerateFiles(directory!)).Where(path => LegacyName.IsMatch(Path.GetFileName(path))))
                candidates.Add(await ReadFileAsync(path, token).ConfigureAwait(false));
            foreach (var group in candidates.GroupBy(bytes => Convert.ToHexString(SHA256.HashData(bytes))).OrderByDescending(group => group.First().Length))
            {
                var original = group.First();
                var applied = Append(original, null);
                if (applied.Length == original.Length) continue;
                var beforePath = Path.Combine(backups, "legacy-before-" + group.Key + ".txt");
                var afterPath = Path.Combine(backups, "legacy-after-" + group.Key + ".txt");
                var donePath = Path.Combine(backups, "legacy-" + group.Key + ".done");
                if (Path.Exists(donePath))
                {
                    if ((await ReadFileAsync(donePath, token).ConfigureAwait(false)).Length != 0) throw new ZapretCompatibilityOwnershipException();
                    continue;
                }
                if (Path.Exists(afterPath))
                {
                    var before = await ReadFileAsync(beforePath, token).ConfigureAwait(false);
                    var after = await ReadFileAsync(afterPath, token).ConfigureAwait(false);
                    if (!HasPrefix(before, applied) || !RemoveLegacyPrefix(before, original, applied).AsSpan().SequenceEqual(after))
                        throw new ZapretCompatibilityOwnershipException();
                    if (result.AsSpan().SequenceEqual(before)) result = after;
                    else if (!result.AsSpan().SequenceEqual(after)) throw new ZapretCompatibilityOwnershipException();
                    legacyCompletions.Add(donePath);
                    continue;
                }
                if (HasPrefix(result, applied))
                {
                    var after = RemoveLegacyPrefix(result, original, applied);
                    // A durable receipt consumes this unmarked range once, preserving identical domains added later.
                    if (!inspectOnly)
                    {
                        if (!Path.Exists(beforePath)) await SaveAsync(backups, beforePath, result, token).ConfigureAwait(false);
                        else if (!(await ReadFileAsync(beforePath, token).ConfigureAwait(false)).AsSpan().SequenceEqual(result)) throw new ZapretCompatibilityOwnershipException();
                        await SaveAsync(backups, afterPath, after, token).ConfigureAwait(false);
                    }
                    legacyCompletions.Add(donePath);
                    result = after;
                }
                else
                {
                    var remainingDomains = DomainLines(StrictUtf8.GetString(result));
                    var userDomains = DomainLines(StrictUtf8.GetString(original));
                    if (Domains.Any(domain => !userDomains.Contains(domain) && remainingDomains.Contains(domain)))
                        throw new ZapretCompatibilityOwnershipException();
                }
            }
        }
        return result;
    }

    private static async Task CompleteLegacyAsync(string backups, List<string> paths, CancellationToken token)
    {
        foreach (var path in paths)
        {
            if (!Path.Exists(path)) await SaveAsync(backups, path, Array.Empty<byte>(), token).ConfigureAwait(false);
            else if ((await ReadFileAsync(path, token).ConfigureAwait(false)).Length != 0) throw new ZapretCompatibilityOwnershipException();
        }
    }

    private static bool HasPrefix(byte[] bytes, byte[] prefix) => bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static byte[] RemoveLegacyPrefix(byte[] bytes, byte[] original, byte[] applied)
    {
        var tail = bytes.AsSpan(applied.Length).ToArray();
        var separator = tail.Length > 0 && NeedsSeparator(original) ? StrictUtf8.GetBytes(Newline(StrictUtf8.GetString(original))) : Array.Empty<byte>();
        return original.Concat(separator).Concat(tail).ToArray();
    }

    private static byte[] RemoveManagedComments(byte[] bytes)
    {
        var text = StrictUtf8.GetString(bytes);
        while (text.IndexOf(BeginMarker, StringComparison.Ordinal) is var start && start >= 0)
        {
            var header = Regex.Match(text[start..], @"\A# OrbitLauncher Zapret compatibility BEGIN ([0-9a-f]{32})(\r\n|\n|\r)", RegexOptions.CultureInvariant);
            if (!header.Success || (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r' && text[start - 1] != '\uFEFF'))
                throw new ZapretCompatibilityOwnershipException();
            var newline = header.Groups[2].Value;
            var ending = "# OrbitLauncher Zapret compatibility END " + header.Groups[1].Value + newline;
            var payloadStart = start + header.Length;
            var end = text.IndexOf(ending, payloadStart, StringComparison.Ordinal);
            if (end < 0) throw new ZapretCompatibilityOwnershipException();
            var lines = text[payloadStart..end].Split(new[] { newline }, StringSplitOptions.None);
            if (lines.Length < 2 || lines[^1].Length != 0 || lines.Length > Domains.Count + 1 ||
                lines[..^1].Any(line => !Domains.Contains(line, StringComparer.Ordinal)) || lines[..^1].Distinct(StringComparer.Ordinal).Count() != lines.Length - 1)
                throw new ZapretCompatibilityOwnershipException();
            text = text.Remove(start, end + ending.Length - start);
        }
        if (text.Contains("# OrbitLauncher Zapret compatibility END ", StringComparison.Ordinal)) throw new ZapretCompatibilityOwnershipException();
        return StrictUtf8.GetBytes(text);
    }

    private static async Task ImportLegacyAsync(string root, string lists, string target, string backups, CancellationToken token)
    {
        var legacy = Path.Combine(root, ".orbit-launcher-backups");
        if (!Directory.Exists(legacy)) return;
        foreach (var source in Directory.EnumerateFiles(legacy).Where(path => LegacyName.IsMatch(Path.GetFileName(path))))
        {
            token.ThrowIfCancellationRequested();
            ValidatePaths(root, lists, target, backups);
            var bytes = await ReadFileAsync(source, token).ConfigureAwait(false);
            var destination = Path.Combine(backups, Path.GetFileName(source));
            if (!File.Exists(destination)) await SaveAsync(backups, destination, bytes, token).ConfigureAwait(false);
            if (!(await ReadFileAsync(destination, token).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes))
                throw new IOException("Zapret legacy backup conflicts with its external copy.");
            token.ThrowIfCancellationRequested();
            ValidatePaths(root, lists, target, backups);
            await EnsureUnchangedAsync(source, bytes, token).ConfigureAwait(false);
            File.Delete(source);
        }
        RequireDirectory(legacy);
        if (!Directory.EnumerateFileSystemEntries(legacy).Any()) Directory.Delete(legacy);
    }

    private static HashSet<string> DomainLines(string text)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var lines = new StringReader(text.TrimStart('\uFEFF'));
        while (lines.ReadLine() is { } line) existing.Add(line.Trim());
        return existing;
    }

    private static string Newline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : Environment.NewLine;
    private static bool NeedsSeparator(byte[] bytes) => bytes.Length > 0 && bytes[^1] != '\n' && bytes[^1] != '\r';
    private static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadFileAsync(string path, CancellationToken token)
    {
        RequireFile(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous);
        return await ReadAsync(stream, token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadAsync(FileStream stream, CancellationToken token)
    {
        if (stream.Length > MaximumListBytes) throw new InvalidDataException("Zapret exclusion list exceeds 1 MiB.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        _ = StrictUtf8.GetString(bytes);
        return bytes;
    }

    private static async Task EnsureUnchangedAsync(string target, byte[] original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bytes = await ReadFileAsync(target, token).ConfigureAwait(false);
        if (!bytes.AsSpan().SequenceEqual(original)) throw new IOException("Zapret exclusion list changed during repair. Retry the repair.");
        // Another process can still replace the file after this check; File.Replace has no compare-and-swap API.
    }

    private static void ValidatePaths(string root, string lists, string target, string backups)
    {
        RequireDirectory(root);
        RequireFile(Path.Combine(root, "service.bat"));
        RequireDirectory(Path.Combine(root, "bin"));
        RequireFile(Path.Combine(root, "bin", "winws.exe"));
        RequireDirectory(lists);
        RequireFile(target);
        RejectLinks(backups);
        if (Path.Exists(backups)) RequireDirectory(backups);
        var legacy = Path.Combine(root, ".orbit-launcher-backups");
        RejectLinks(legacy);
        if (Path.Exists(legacy)) RequireDirectory(legacy);
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Zapret paths must not be links: " + current);
            }
            else if (new FileInfo(current).LinkTarget != null || new DirectoryInfo(current).LinkTarget != null)
                throw new InvalidDataException("Zapret paths must not be links: " + current);
        }
    }

    private static void RequireDirectory(string path)
    {
        RejectLinks(path);
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0) throw new DirectoryNotFoundException("Zapret folder not found: " + path);
    }

    private static void RequireFile(string path)
    {
        RejectLinks(path);
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new InvalidDataException("Zapret requires a regular file: " + path);
    }

    private static async Task SaveAsync(string backups, string path, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RejectLinks(backups);
        Directory.CreateDirectory(backups);
        RequireDirectory(backups);
        RejectLinks(path);
        var temporary = Path.Combine(backups, ".pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            await WriteNewAsync(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RequireDirectory(backups);
            RejectLinks(path);
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) { RequireFile(temporary); File.Delete(temporary); }
        }
    }

    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }
}
