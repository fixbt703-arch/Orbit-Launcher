using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services.Connect;

public sealed class EasyTierTransport : IAsyncDisposable
{
    private readonly ConnectProcess _process;
    private readonly ConnectRuntimePaths _runtime;
    private readonly string _directory;
    private readonly int _rpc;
    public int LocalPort { get; }
    public bool IsAlive => _process.IsAlive;
    private EasyTierTransport(ConnectProcess process, ConnectRuntimePaths runtime, string directory, int rpc, int localPort)
    { _process = process; _runtime = runtime; _directory = directory; _rpc = rpc; LocalPort = localPort; }

    public static async Task<EasyTierTransport> StartAsync(ConnectRuntimePaths runtime, ConnectInvitation invitation, bool host,
        IReadOnlyList<ConnectNode> nodes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string directory = Path.Combine(AppPaths.Root, "connect", "sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
        int localPort = host ? 0 : FreePort(), rpc = FreePort();
        byte[] address = RandomNumberGenerator.GetBytes(2);
        string ipv4 = host ? "10.144.144.1" : $"10.144.{address[0] % 128}.{2 + address[1] % 250}";
        var config = new StringBuilder();
        config.AppendLine("instance_name = \"OrbitConnect\"");
        config.AppendLine("ipv4 = " + JsonSerializer.Serialize(ipv4));
        config.AppendLine("listeners = [\"udp://0.0.0.0:0\"]");
        config.AppendLine("rpc_portal = " + JsonSerializer.Serialize("127.0.0.1:" + rpc));
        config.AppendLine("tcp_whitelist = [\"" + (host ? invitation.Port : 0) + "\"]\nudp_whitelist = [\"0\"]");
        if (!host) config.AppendLine($"[[port_forward]]\nproto = \"tcp\"\nbind_addr = \"127.0.0.1:{localPort}\"\ndst_addr = \"10.144.144.1:{invitation.Port}\"");
        config.AppendLine("[network_identity]\nnetwork_name = " + JsonSerializer.Serialize(invitation.NetworkName)
            + "\nnetwork_secret = " + JsonSerializer.Serialize(invitation.Key));
        foreach (var node in nodes) config.AppendLine("[[peer]]\nuri = " + JsonSerializer.Serialize(node.Endpoint));
        config.AppendLine("[flags]\nno_tun = true\nbind_device = false\nlatency_first = false\ndefault_protocol = \"udp\"\ndisable_tcp_hole_punching = true\nrelay_network_whitelist = \"\"");
        string path = Path.Combine(directory, "session.toml"); await File.WriteAllTextAsync(path, config.ToString(), token);
        ConnectProcess? process = null;
        try
        {
            process = new(runtime.Core, ["-c", path, "-r", "127.0.0.1:" + rpc], directory, invitation.Key, invitation.Encode());
            await Task.Delay(500, token);
            if (!process.IsAlive) throw new IOException("Сетевой компонент завершился (" + process.ExitCode.ToString("X8") + "): " + process.LastError);
            return new(process, runtime, directory, rpc, localPort);
        }
        catch { if (process is not null) await process.DisposeAsync(); File.Delete(path); throw; }
    }

    public Task<string> PeerInfoAsync(CancellationToken token) => RunCliAsync(["peer"], token);
    public async Task AddConnectorAsync(ConnectNode node, CancellationToken token)
    {
        if (!ConnectNodeCatalog.Load().Contains(node) && node.Source != "local-test") throw new IOException("Неизвестный резервный узел.");
        await RunCliAsync(["connector", "add", node.Endpoint], token);
    }
    private async Task<string> RunCliAsync(string[] arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(_runtime.Cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-p", "127.0.0.1:" + _rpc, "-o", "json" }.Concat(arguments)) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось прочитать маршрут.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token); await stderr;
            string result = await stdout;
            if (process.ExitCode != 0 || result.Length > 262144) throw new IOException("Маршрут пока не готов.");
            return result;
        }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }

    public static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync();
        try { File.Delete(Path.Combine(_directory, "session.toml")); Directory.Delete(_directory); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
