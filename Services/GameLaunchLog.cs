using System.Text;
using System.Text.RegularExpressions;

namespace OrbitLauncher.Services;

public sealed class GameLaunchLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly string[] _secrets;
    private long _written;
    private bool _disposed;
    public string FilePath { get; }

    public GameLaunchLog(string instanceDirectory, params string?[] secrets)
    {
        var directory = Path.Combine(instanceDirectory, ".orbit", "logs"); Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "launch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        _secrets = secrets.Where(value => !string.IsNullOrEmpty(value)).Select(value => value!).OrderByDescending(value => value.Length).ToArray();
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            if (_disposed || _written >= 8 * 1024 * 1024) return;
            foreach (var secret in _secrets) message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
            message = Regex.Replace(message, @"(?i)(Bearer\s+|(?:accessToken|clientToken|refreshToken|password|authorization)(?:\s*[=:]\s*|\s+)(?:Bearer\s+)?)[^\s,;]+", "$1[redacted]");
            try { _writer.WriteLine(message); _written += Encoding.UTF8.GetByteCount(message) + 2; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            try { _writer.Dispose(); } catch (IOException) { }
        }
    }
}
