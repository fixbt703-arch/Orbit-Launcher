using System.Collections.Concurrent;
using System.Text.Json;

namespace OrbitLauncher.Services;

/// <summary>Serializes writers per file and replaces complete JSON documents atomically.</summary>
public static class AtomicJsonFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static SemaphoreSlim Gate(string path) => Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    public static T? Read<T>(string path) where T : class
    {
        var gate = Gate(path); gate.Wait();
        try { return ReadCore<T>(path); }
        finally { gate.Release(); }
    }

    private static T? ReadCore<T>(string path) where T : class
    {
        var backup = path + ".bak";
        if (!File.Exists(path)) return File.Exists(backup) ? Deserialize<T>(backup) : null;
        try { return Deserialize<T>(path); }
        catch (JsonException)
        {
            // Keep the damaged document for recovery even if a later save replaces it.
            var damaged = File.ReadAllBytes(path);
            var recoveryPath = path + ".corrupt-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(damaged)).Substring(0, 16);
            if (!File.Exists(recoveryPath)) File.WriteAllBytes(recoveryPath, damaged);
            if (File.Exists(backup))
            {
                var restored = Deserialize<T>(backup);
                var temporary = path + ".recover-" + Guid.NewGuid().ToString("N");
                try { File.Copy(backup, temporary); File.Move(temporary, path, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return restored;
            }
            throw;
        }
    }

    private static T? Deserialize<T>(string path) where T : class
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(stream, JsonDefaults.Options);
    }

    public static async Task SaveAsync<T>(string path, T value) where T : class
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        var gate = Gate(path); await gate.WaitAsync().ConfigureAwait(false);
        try { await WriteCoreAsync(path, bytes).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public static async Task UpdateAsync<T>(string path, Func<T?, T> update) where T : class
    {
        var gate = Gate(path); await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(update(ReadCore<T>(path)), JsonDefaults.Options);
            await WriteCoreAsync(path, bytes).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static async Task WriteCoreAsync(string path, byte[] bytes)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
