using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using OrbitLauncher.Services.Connect;

internal static class ConnectProductionSmoke
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = deadline.Token;
        var runtime = await new ConnectRuntimeInstaller(Path.Combine(output, "runtime")).EnsureAsync(token);
        var echo = new TcpListener(IPAddress.Loopback, 0); echo.Start();
        int gamePort = ((IPEndPoint)echo.LocalEndpoint).Port, relayPort = EasyTierTransport.FreePort();
        var info = new ProcessStartInfo(runtime.Core) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { "--network-name", "fixture-" + Guid.NewGuid().ToString("N"), "--network-secret", "test-relay-secret", "--listeners", "tcp://127.0.0.1:" + relayPort, "--no-tun", "--bind-device", "false", "--rpc-portal", "0" }) info.ArgumentList.Add(a);
        using var relay = Process.Start(info)!;
        var stdout = relay.StandardOutput.ReadToEndAsync(); var stderr = relay.StandardError.ReadToEndAsync();
        EasyTierTransport? host = null, guest = null;
        try
        {
            await Task.Delay(500, token);
            var invitation = ConnectInvitation.Create(gamePort, "Production socket test", "26.2");
            var nodes = new[] { new ConnectNode("fixture", "fixture", "tcp://127.0.0.1:" + relayPort, true, "local-test") };
            host = await EasyTierTransport.StartAsync(runtime, invitation, true, nodes, token);
            guest = await EasyTierTransport.StartAsync(runtime, invitation, false, nodes, token);
            await Task.Delay(2500, token);
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, guest.LocalPort, token);
            byte[] sent = RandomNumberGenerator.GetBytes(4096), returned = new byte[4096];
            await client.GetStream().WriteAsync(sent, token);
            using var server = await echo.AcceptTcpClientAsync(token);
            await server.GetStream().ReadExactlyAsync(returned, token);
            if (!sent.SequenceEqual(returned)) throw new Exception("Production transport corrupted payload");
            await server.GetStream().WriteAsync(returned, token);
            await client.GetStream().ReadExactlyAsync(returned, token);
            if (!sent.SequenceEqual(returned)) throw new Exception("Production reverse transport corrupted payload");
            string peers = await guest.PeerInfoAsync(token);
            await File.WriteAllTextAsync(Path.Combine(output, "peers.json"), peers, token);
            await guest.AddConnectorAsync(nodes[0], token);
            var other = new TcpListener(IPAddress.Loopback, 0); other.Start();
            try
            {
                await using var blocked = await EasyTierTransport.StartAsync(runtime,
                    invitation with { Port = ((IPEndPoint)other.LocalEndpoint).Port }, false, nodes, token);
                await Task.Delay(1000, token);
                using var attempt = new TcpClient(); await attempt.ConnectAsync(IPAddress.Loopback, blocked.LocalPort, token);
                await attempt.GetStream().WriteAsync(new byte[] { 1, 2, 3 }, token); await Task.Delay(700, token);
                if (other.Pending()) throw new Exception("Unlisted host port exposed through overlay");
            }
            finally { other.Stop(); }
            await using (var wrongKey = await EasyTierTransport.StartAsync(runtime, ConnectInvitation.Create(gamePort, "Wrong secret", "26.2"), false, nodes, token))
            {
                using var attempt = new TcpClient(); await attempt.ConnectAsync(IPAddress.Loopback, wrongKey.LocalPort, token);
                await attempt.GetStream().WriteAsync(new byte[] { 1, 2, 3 }, token); await Task.Delay(1000, token);
                if (echo.Pending()) throw new Exception("Wrong room secret reached game port");
            }
            Console.WriteLine("PASS production transport: pinned upstream downloads, hashes, private config, real forward and reverse stream, peer RPC");
            Console.WriteLine("PASS native isolation: live connector CLI, different room secret rejected, unlisted host listener inaccessible");
        }
        finally
        {
            echo.Stop(); if (guest is not null) await guest.DisposeAsync(); if (host is not null) await host.DisposeAsync();
            if (!relay.HasExited) relay.Kill(entireProcessTree: true); await relay.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
        }
    }
}
