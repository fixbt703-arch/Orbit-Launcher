using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OrbitLauncher.Services.Connect;

public sealed class ConnectControlServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(12, 12);
    private readonly Func<string, string, Task<string>> _handler;
    private readonly string _authorization;
    private readonly Task _loop;
    public string Address { get; }
    public ConnectControlServer(Func<string, string, Task<string>> handler, string token)
    {
        _handler = handler; _authorization = "Bearer " + token;
        _listener.Start(16); Address = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";
        _loop = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (!_slots.Wait(0)) { client.Dispose(); continue; }
                _ = HandleAsync(client);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(6)); var token = deadline.Token;
            try
            {
                client.NoDelay = true; var stream = client.GetStream(); var header = new List<byte>(); byte[] next = new byte[1];
                while (header.Count < 8192)
                {
                    if (await stream.ReadAsync(next, token) == 0) return;
                    header.Add(next[0]);
                    if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool duplicate = false;
                foreach (string line in lines.Skip(1).Where(s => s.Contains(':')))
                { int split = line.IndexOf(':'); if (!headers.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim())) duplicate = true; }
                int status = 200; string response;
                if (header.Count >= 8192 || request.Length != 3 || request[0] is not ("GET" or "POST") || duplicate)
                { status = 400; response = "{}"; }
                else if (headers.ContainsKey("Origin") || headers.ContainsKey("Transfer-Encoding")
                    || !headers.TryGetValue("Authorization", out var auth)
                    || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth), Encoding.UTF8.GetBytes(_authorization)))
                { status = 403; response = "{}"; }
                else
                {
                    int size = 0;
                    if (headers.TryGetValue("Content-Length", out var length) && (!int.TryParse(length, out size) || size is < 0 or > 4096))
                    { status = 413; response = "{}"; }
                    else
                    {
                        byte[] body = new byte[size]; await stream.ReadExactlyAsync(body, token);
                        response = await _handler(request[1].TrimStart('/'), Encoding.UTF8.GetString(body));
                    }
                }
                byte[] bytes = Encoding.UTF8.GetBytes(response);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, token); await stream.WriteAsync(bytes, token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or FormatException or System.Text.Json.JsonException) { }
            finally { _slots.Release(); }
        }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; }
}
