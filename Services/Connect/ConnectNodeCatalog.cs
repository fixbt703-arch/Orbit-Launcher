using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace OrbitLauncher.Services.Connect;

public sealed record ConnectNode(string Id, string Owner, string Endpoint, bool Relay, string Source);

public static class ConnectNodeCatalog
{
    public static IReadOnlyList<ConnectNode> Load()
    {
        using var stream = typeof(ConnectNodeCatalog).Assembly.GetManifestResourceStream("OrbitLauncher.Connect.Nodes")
            ?? throw new InvalidDataException("Список узлов Orbit Connect отсутствует.");
        return JsonSerializer.Deserialize<ConnectNode[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Повреждён список узлов Orbit Connect.");
    }

    public static async Task<IReadOnlyList<ConnectNode>> OrderAsync(CancellationToken token)
    {
        var candidates = await Task.WhenAll(Load().Select(async node =>
        {
            var uri = new Uri(node.Endpoint); var watch = Stopwatch.StartNew();
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(3));
                using var client = new TcpClient();
                await client.ConnectAsync(uri.Host, uri.IsDefaultPort ? 443 : uri.Port, limit.Token);
                return (Node: node, Delay: watch.Elapsed.TotalMilliseconds);
            }
            catch (Exception e) when (e is SocketException or IOException or OperationCanceledException) { return (Node: node, Delay: double.MaxValue); }
        }));
        token.ThrowIfCancellationRequested();
        // One transport per owner at startup avoids measuring the same idle/busy peer twice.
        return candidates.OrderBy(x => x.Delay).GroupBy(x => x.Node.Owner).Select(g => g.First().Node).ToArray();
    }
}
