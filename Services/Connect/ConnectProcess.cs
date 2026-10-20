using System.Diagnostics;

namespace OrbitLauncher.Services.Connect;

internal sealed class ConnectProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ConnectProcessJob _job;
    private readonly Task _stdout, _stderr;
    private readonly Queue<string> _tail = new();
    private readonly List<string> _first = new();
    public bool IsAlive { get { try { return !_process.HasExited; } catch (InvalidOperationException) { return false; } } }
    public int Id => _process.Id;
    public int ExitCode => _process.ExitCode;
    public string LastError { get { lock (_tail) return string.Join(" ", _first.Concat(_tail.TakeLast(2))); } }

    public ConnectProcess(string executable, IEnumerable<string> arguments, string workingDirectory, params string[] secrets)
        : this(executable, arguments, workingDirectory, null, secrets) { }
    public ConnectProcess(string executable, IEnumerable<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] secrets)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null) foreach (var item in environment) info.Environment[item.Key] = item.Value;
        _process = Process.Start(info) ?? throw new IOException("Не удалось запустить сетевой компонент.");
        try { _job = new ConnectProcessJob(_process); }
        catch { if (!_process.HasExited) _process.Kill(entireProcessTree: true); _process.Dispose(); throw; }
        _stdout = DrainAsync(_process.StandardOutput, secrets); _stderr = DrainAsync(_process.StandardError, secrets);
    }
    private async Task DrainAsync(StreamReader reader, string[] secrets)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                foreach (var secret in secrets.Where(s => s.Length > 0)) line = line.Replace(secret, "[redacted]", StringComparison.Ordinal);
                if (line.Length > 1500) line = line[..1500];
                lock (_tail) { if (_first.Count < 4) _first.Add(line); _tail.Enqueue(line); if (_tail.Count > 12) _tail.Dequeue(); }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync(); }
        catch (InvalidOperationException) { }
        _job.Dispose(); await Task.WhenAll(_stdout, _stderr); _process.Dispose();
    }
}
