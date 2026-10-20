using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

internal static class ConnectTransportSmoke
{
    public static async Task RunAsync(string runtime, string output, int seconds = 20, string? publicPeer = null)
    {
        Directory.CreateDirectory(output);
        string core = Directory.GetFiles(runtime, "easytier-core.exe", SearchOption.AllDirectories).Single();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 100));
        var token = deadline.Token;
        var echo = new TcpListener(IPAddress.Loopback, 0); echo.Start();
        int gamePort = ((IPEndPoint)echo.LocalEndpoint).Port;
        int relay1 = Port(), relay2 = Port(), localPort = Port();
        string network = "orbit-test-" + Guid.NewGuid().ToString("N");
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var processes = new List<Process>();
        var logTasks = new List<Task>();
        Process Start(string label, params string[] arguments)
        {
            var info = new ProcessStartInfo(core) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = output };
            foreach (string arg in arguments) info.ArgumentList.Add(arg);
            info.ArgumentList.Add("--bind-device"); info.ArgumentList.Add("false");
            info.ArgumentList.Add("--rpc-portal"); info.ArgumentList.Add("0");
            var process = Process.Start(info) ?? throw new Exception("Native core did not start");
            processes.Add(process);
            async Task Drain(StreamReader reader, string suffix)
            {
                await using var writer = File.CreateText(Path.Combine(output, label + suffix + ".txt"));
                while (await reader.ReadLineAsync() is { } line)
                    await writer.WriteLineAsync(line.Replace(secret, "[redacted]"));
            }
            logTasks.Add(Drain(process.StandardOutput, "-out")); logTasks.Add(Drain(process.StandardError, "-err"));
            return process;
        }
        var echoTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var peer = await echo.AcceptTcpClientAsync(token);
                    _ = Task.Run(async () =>
                    {
                        using (peer)
                        {
                            try { var stream = peer.GetStream(); var bytes = new byte[32768];
                                while (true) { int read = await stream.ReadAsync(bytes, token); if (read == 0) return;
                                    await stream.WriteAsync(bytes.AsMemory(0, read), token); } }
                            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { }
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
        });
        try
        {
            Process? firstRelay = null;
            if (publicPeer is null)
            {
                firstRelay = Start("relay-a", "--network-name", "relay-a-" + network, "--network-secret", secret,
                    "--listeners", "tcp://127.0.0.1:" + relay1, "--no-tun");
                Start("relay-b", "--network-name", "relay-b-" + network, "--network-secret", secret,
                    "--listeners", "tcp://127.0.0.1:" + relay2, "--no-tun");
            }
            await Task.Delay(1000, token);
            string[] Common(string address) => ["--network-name", network, "--network-secret", secret,
                "--ipv4", address, "--no-tun", "--no-listener", "--disable-p2p", "true",
                "--disable-udp-hole-punching", "true", "--disable-tcp-hole-punching", "true",
                "--peers", publicPeer ?? "tcp://127.0.0.1:" + relay1, publicPeer ?? "tcp://127.0.0.1:" + relay2,
                "--compression", "none", "--tcp-whitelist", gamePort.ToString(), "--udp-whitelist", "0"];
            Start("host", Common("10.144.144.1"));
            Start("guest", [.. Common("10.144.144.2"), "--port-forward",
                $"tcp://127.0.0.1:{localPort}/10.144.144.1:{gamePort}"]);
            await Task.Delay(4000, token);
            foreach (var p in processes) if (p.HasExited) throw new Exception("Core exited: " + p.ExitCode);
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, localPort, token);
            var stream = client.GetStream();
            byte[] data = RandomNumberGenerator.GetBytes(16384), returned = new byte[data.Length];
            var watch = Stopwatch.StartNew(); var latency = new List<double>(); int frames = 0;
            bool relayStopped = false;
            while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                if (firstRelay is not null && !relayStopped && watch.Elapsed >= TimeSpan.FromSeconds(seconds / 2.0))
                {
                    firstRelay.Kill(entireProcessTree: true); await firstRelay.WaitForExitAsync(token);
                    relayStopped = true;
                    Console.WriteLine("Injected failure: own relay-a stopped; same guest TCP socket remains open.");
                }
                var roundtrip = Stopwatch.StartNew();
                await stream.WriteAsync(data, token);
                await stream.ReadExactlyAsync(returned, token);
                if (!data.AsSpan().SequenceEqual(returned)) throw new Exception("Corrupt or reordered game stream");
                latency.Add(roundtrip.Elapsed.TotalMilliseconds); frames++;
                await Task.Delay(50, token);
            }
            latency.Sort();
            string result = $"PASS native EasyTier relay transfer: {frames} frames, {frames * data.Length} bytes; {(publicPeer is null ? "same TCP connection survived own relay failure" : "public relay " + publicPeer)}; RTT p50={latency[latency.Count / 2]:F2}ms p95={latency[(int)(latency.Count * .95)]:F2}ms max={latency[^1]:F2}ms; duration={watch.Elapsed.TotalSeconds:F1}s. Same machine peers, not two ISP networks.";
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), result);
            Console.WriteLine(result);
        }
        finally
        {
            deadline.Cancel(); echo.Stop();
            foreach (var p in processes) { try { if (!p.HasExited) p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); } catch (InvalidOperationException) { } }
            await Task.WhenAll(logTasks); await echoTask;
            foreach (var p in processes) p.Dispose();
        }
    }

    private static int Port() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
}
