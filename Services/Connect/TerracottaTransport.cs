using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Net;
using System.Net.Sockets;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services.Connect;

public sealed class TerracottaTransport : IAsyncDisposable
{
    private readonly ConnectProcess _process;
    private readonly HttpClient _http;
    private readonly string _portFile;
    private readonly string _directory;
    public bool IsAlive => _process.IsAlive;
    private string _phase = "waiting";
    private static string PublicNodes => string.Concat(ConnectNodeCatalog.Load().Where(n => n.Endpoint.StartsWith("tcp://", StringComparison.Ordinal))
        .Select(n => "&public_nodes=" + Uri.EscapeDataString(n.Endpoint)));
    private TerracottaTransport(ConnectProcess process, HttpClient http, string portFile, string directory) { _process = process; _http = http; _portFile = portFile; _directory = directory; }
    public static bool IsRoomCode(string code) => Regex.IsMatch(code, "^U/[0-9A-HJ-NP-Z]{4}(-[0-9A-HJ-NP-Z]{4}){3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static async Task<TerracottaTransport> StartAsync(ConnectRuntimePaths runtime, CancellationToken token)
    {
        string directory = ConnectDirectories.CreatePrivate("terracotta-sessions");
        string portFile = Path.Combine(directory, "tc-" + Guid.NewGuid().ToString("N") + ".json");
        var process = new ConnectProcess(runtime.Terracotta, ["--hmcl2", portFile], Path.GetDirectoryName(runtime.Terracotta)!,
            new Dictionary<string, string> { ["TEMP"] = directory, ["TMP"] = directory });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
            while (!File.Exists(portFile))
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!process.IsAlive) throw new IOException("Terracotta не запустилась или уже используется другим приложением.");
                await Task.Delay(150, timeout.Token);
            }
            using var data = JsonDocument.Parse(await File.ReadAllTextAsync(portFile, timeout.Token));
            int port = data.RootElement.GetProperty("port").GetInt32();
            if (port is < 1 or > 65535 || !process.IsAlive || !ConnectGamePorts.IsOwned(port, process.Id))
                throw new IOException("Terracotta уже используется другим приложением.");
            var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
                { BaseAddress = new Uri("http://127.0.0.1:" + port + "/"), Timeout = TimeSpan.FromSeconds(3) };
            return new(process, http, portFile, directory);
        }
        catch { await process.DisposeAsync(); ConnectDirectories.DeleteOwned(directory, "terracotta-sessions"); throw; }
    }
    public async Task<string> JoinAsync(string code, CancellationToken token)
    {
        if (!IsRoomCode(code)) throw new FormatException("Некорректный код комнаты Terracotta.");
        using var response = await _http.GetAsync("state/guesting?room=" + Uri.EscapeDataString(code) + PublicNodes, token); response.EnsureSuccessStatusCode();
        return await WaitAsync("guest-ok", "url", token);
    }
    public async Task<string> HostAsync(int expectedPort, int gamePid, CancellationToken token)
    {
        using var response = await _http.GetAsync("state/scanning?" + PublicNodes.TrimStart('&'), token); response.EnsureSuccessStatusCode();
        string code = await WaitAsync("host-ok", "room", token);
        // Upstream scans all LAN games. Verify its actual selected port before sharing the room.
        using var meta = JsonDocument.Parse(await _http.GetStringAsync("meta", token));
        int controlPort = meta.RootElement.GetProperty("yggdrasil_port").GetInt32();
        if (!ConnectGamePorts.IsOwned(controlPort, _process.Id)) throw new IOException("Не удалось проверить резервную комнату.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var client = new TcpClient { NoDelay = true }; await client.ConnectAsync(IPAddress.Loopback, controlPort, deadline.Token);
        byte[] method = Encoding.ASCII.GetBytes("c:server_port");
        byte[] request = new byte[method.Length + 5]; request[0] = (byte)method.Length; method.CopyTo(request, 1);
        var stream = client.GetStream(); await stream.WriteAsync(request, deadline.Token);
        byte[] reply = new byte[7]; await stream.ReadExactlyAsync(reply, deadline.Token);
        int port = (reply[5] << 8) | reply[6];
        if (!reply.AsSpan(0, 5).SequenceEqual(new byte[] { 0, 0, 0, 0, 2 }) || port != expectedPort || !ConnectGamePorts.IsOwned(port, gamePid))
            throw new IOException("Резерв отключён: Terracotta выбрала другой LAN-мир.");
        if (!IsRoomCode(code)) throw new IOException("Некорректный код резервной комнаты.");
        return code;
    }
    private async Task<string> WaitAsync(string phase, string field, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (timeout.IsCancellationRequested && !token.IsCancellationRequested) throw new IOException("Terracotta: время ожидания истекло (" + _phase + ").");
            token.ThrowIfCancellationRequested();
            string json = await _http.GetStringAsync("state", timeout.Token);
            if (json.Length > 32768) throw new InvalidDataException("Некорректный ответ Terracotta.");
            using var data = JsonDocument.Parse(json); string state = data.RootElement.GetProperty("state").GetString() ?? "";
            _phase = state;
            if (state == phase) return data.RootElement.GetProperty(field).GetString() ?? throw new IOException("Terracotta не вернула адрес комнаты.");
            if (state == "exception" || !_process.IsAlive) throw new IOException("Terracotta не смогла соединиться с другом.");
            await Task.Delay(500, timeout.Token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        _http.Dispose(); await _process.DisposeAsync();
        ConnectDirectories.DeleteOwned(_directory, "terracotta-sessions");
    }
}
