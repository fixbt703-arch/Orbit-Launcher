using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using OrbitLauncher.Models;
using OrbitLauncher.Services.Connect;

internal static class ConnectIntegrationSmoke
{
    public static async Task SessionAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150)); var token = timeout.Token;
        var host = await ConnectWorkerClient.ForAsync(new GameInstance { Name = "Host fixture", GameVersion = "26.2", OrbitConnectEnabled = true }, token);
        var guest = await ConnectWorkerClient.ForAsync(new GameInstance { Name = "Guest fixture", GameVersion = "26.2", OrbitConnectEnabled = true }, token);
        Process Worker(ConnectWorkerClient client) => (Process)typeof(ConnectWorkerClient).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        await using var server = new MinecraftStatusFixture();
        using var advertising = CancellationTokenSource.CreateLinkedTokenSource(token);
        var broadcast = Task.Run(async () =>
        {
            using var udp = new UdpClient(); byte[] bytes = Encoding.UTF8.GetBytes("[MOTD]Orbit session fixture[/MOTD][AD]" + server.Port + "[/AD]");
            try { while (!advertising.IsCancellationRequested) { await udp.SendAsync(bytes, new IPEndPoint(IPAddress.Loopback, 4445), advertising.Token); await Task.Delay(500, advertising.Token); } }
            catch (OperationCanceledException) { }
        });
        async Task<ConnectStatus> Wait(ConnectWorkerClient client, string phase)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); var status = await client.RequestAsync("state", token: token);
                if (status.Phase == "error") throw new Exception("Session failed: " + status.Message);
                if (status.Phase == phase) return status;
                await Task.Delay(500, token);
            }
        }
        try
        {
            await host.RequestAsync("attach", new { pid = Environment.ProcessId }, token);
            await guest.RequestAsync("attach", new { pid = Environment.ProcessId }, token);
            await host.RequestAsync("host", new { port = server.Port }, token);
            var hosted = await Wait(host, "hosting"); var invitation = ConnectInvitation.Parse(hosted.Invitation);
            if (invitation.TerracottaCode.Length == 0) throw new Exception("Native Terracotta backup was not prepared");
            await using (var launchFailure = new ConnectSession(new GameInstance()))
            {
                var join = typeof(ConnectSession).GetMethod("JoinAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                // Deliberately bypass the parser to make native setup throw before creating a process.
                await (Task)join.Invoke(launchFailure, [invitation with { Key = "invalid-start-fixture" }, token])!;
                if (launchFailure.Status.Phase != "connected") throw new Exception("Early primary setup failure skipped backup");
                Console.WriteLine("PASS fallback boundary: early primary setup exception still tries valid native backup");
            }
            await guest.RequestAsync("join", new { code = hosted.Invitation }, token);
            var joined = await Wait(guest, "connected");
            int port = int.Parse(joined.Address[10..]); int requestsBefore = server.Requests, sample = 0;
            try
            {
                for (; sample < 20; sample++) { requestsBefore = server.Requests; await ConnectMinecraftProbe.QueryAsync(port, token); }
            }
            catch
            {
                Console.WriteLine("DIAGNOSTIC initial proxy query: host=" + (await host.RequestAsync("state", token: token)).Phase + ", guest=" + (await guest.RequestAsync("state", token: token)).Phase);
                Console.WriteLine("DIAGNOSTIC failed fresh connection=" + sample + ", world requests during failed proxy probe=" + (server.Requests - requestsBefore));
                Console.WriteLine("DIAGNOSTIC direct fixture RTT=" + await ConnectMinecraftProbe.QueryAsync(server.Port, token));
                foreach (string file in Directory.GetFiles(Path.Combine(OrbitLauncher.Utilities.AppPaths.Root, "connect", "sessions"), "session.toml", SearchOption.AllDirectories))
                {
                    string config = await File.ReadAllTextAsync(file, token);
                    if (!config.Contains(invitation.NetworkName, StringComparison.Ordinal) || !config.Contains("[[port_forward]]", StringComparison.Ordinal)) continue;
                    var match = System.Text.RegularExpressions.Regex.Match(config, "bind_addr = \"127\\.0\\.0\\.1:(\\d+)\"");
                    if (match.Success)
                    {
                        try { Console.WriteLine("DIAGNOSTIC direct EasyTier forward RTT=" + await ConnectMinecraftProbe.QueryAsync(int.Parse(match.Groups[1].Value), token)); }
                        catch (Exception e) { Console.WriteLine("DIAGNOSTIC direct EasyTier failed=" + e.GetType().Name); }
                    }
                }
                try { Console.WriteLine("DIAGNOSTIC second stable proxy RTT=" + await ConnectMinecraftProbe.QueryAsync(port, token)); }
                catch (Exception e) { Console.WriteLine("DIAGNOSTIC second stable proxy failed=" + e.GetType().Name); }
                throw;
            }
            Console.WriteLine("PASS complete session: two headless workers, owned world, invitation, actual Minecraft status over EasyTier and pending proxy");
            if (invitation.TerracottaCode.Length == 0) throw new Exception("Native Terracotta backup was not prepared");
            KillCoreChild(Worker(host).Id); KillCoreChild(Worker(guest).Id);
            await Task.Delay(3000, token);
            var recovered = await Wait(guest, "connected");
            if (recovered.Address != joined.Address) throw new Exception("Recovery changed stable local join address");
            await ConnectMinecraftProbe.QueryAsync(port, token);
            Console.WriteLine("PASS engine fallback: killed only owned primary cores; native Terracotta reached same verified world; local address retained, fresh Minecraft connection succeeds");
            await host.RequestAsync("stop", token: token); await guest.RequestAsync("stop", token: token);
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "PASS controller + native fallback; no real player connection continuity claimed across engine death. Invitations/tokens redacted.", token);
        }
        finally
        {
            advertising.Cancel(); await broadcast;
            foreach (var client in new[] { guest, host }) try { await client.RequestAsync("stop"); } catch (Exception) { }
            foreach (var process in new[] { Worker(guest), Worker(host) }) if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
    public static async Task MonitorRaceAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var token = timeout.Token;
        var runtime = await new ConnectRuntimeInstaller().EnsureAsync(token);
        runtime = runtime with { Cli = Path.Combine(AppContext.BaseDirectory, "OrbitLauncher.Tests.exe") };
        var core = await EasyTierTransport.StartAsync(runtime, ConnectInvitation.Create(25565, "Race fixture", "26.2"), false, [], token);
        int rpc = (int)typeof(EasyTierTransport).GetField("_rpc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(core)!;
        string marker = Path.Combine(AppContext.BaseDirectory, "connect-rpc-" + rpc + ".ready");
        await using var session = new ConnectSession(new GameInstance());
        await session.HandleAsync("attach", "{\"pid\":" + Environment.ProcessId + "}");
        typeof(ConnectSession).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, core);
        try
        {
            while (!File.Exists(marker)) await Task.Delay(20, token);
            await session.StopAsync(); await Task.Delay(1200, token);
            var monitor = (Task)typeof(ConnectSession).GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            if (monitor.IsCompleted || session.Status.Phase != "idle") throw new Exception("Stop during route probe terminated or changed the session monitor");
            Console.WriteLine("PASS monitor race: stopped during delayed RPC, monitor stays alive for future world/game lifecycle");
        }
        finally { File.Delete(marker); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry { public uint Size, Usage, Pid; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static void KillCoreChild(int parent)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0); if (snapshot == new IntPtr(-1)) throw new Exception("Process snapshot unavailable");
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
            if (Process32FirstW(snapshot, ref entry)) do
            {
                if (entry.Parent == parent && entry.Name == "easytier-core.exe") { using var process = Process.GetProcessById((int)entry.Pid); process.Kill(true); process.WaitForExit(); return; }
            } while (Process32NextW(snapshot, ref entry));
            throw new Exception("Owned primary core missing");
        }
        finally { CloseHandle(snapshot); }
    }
    public static async Task WorkerAsync()
    {
        var instance = new GameInstance { Name = "Connect worker fixture", OrbitConnectEnabled = true };
        var client = await ConnectWorkerClient.ForAsync(instance);
        var process = (Process)typeof(ConnectWorkerClient).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        try
        {
            var status = await client.RequestAsync("state");
            if (status.Phase != "idle" || process.MainWindowHandle != IntPtr.Zero) throw new Exception("Worker has UI or bad state");
            await client.RequestAsync("attach", new { pid = Environment.ProcessId });
            status = await client.RequestAsync("host", new { port = 1 });
            if (status.Phase != "error") throw new Exception("Foreign game port accepted by worker");
            Console.WriteLine("PASS worker: same launcher EXE, no window, authenticated real IPC, foreign port rejected");
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    public static async Task TerracottaAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = timeout.Token;
        var runtime = await new ConnectRuntimeInstaller(Path.Combine(output, "runtime")).EnsureAsync(token);
        await using var server = new MinecraftStatusFixture();
        await using var transport = await TerracottaTransport.StartAsync(runtime, token);
        var advertise = Task.Run(async () =>
        {
            using var udp = new UdpClient(); byte[] packet = Encoding.UTF8.GetBytes("[MOTD]Orbit isolated fixture[/MOTD][AD]" + server.Port + "[/AD]");
            try { while (!token.IsCancellationRequested) { await udp.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, 4445), token); await Task.Delay(500, token); } }
            catch (OperationCanceledException) { }
        });
        try
        {
            string code = await transport.HostAsync(server.Port, Environment.ProcessId, token);
            if (!TerracottaTransport.IsRoomCode(code)) throw new Exception("Invalid native room");
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "PASS native Terracotta: private TEMP, owned process/API, host room, selected game port verified by scaffolding RPC. Invitation redacted.");
            Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(output, "result.txt")));
        }
        finally { timeout.Cancel(); await advertise; }
    }
}

internal sealed class MinecraftStatusFixture : IAsyncDisposable
{
    private int _requests;
    public int Requests => Volatile.Read(ref _requests);
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly bool _legacyOnly;
    public int Port { get; }
    public MinecraftStatusFixture(bool legacyOnly = false) { _legacyOnly = legacyOnly; _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _loop = LoopAsync(); }
    private async Task LoopAsync()
    {
        try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = HandleAsync(client); } }
        catch (Exception e) when (e is OperationCanceledException or SocketException) { }
    }
    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var stream = client.GetStream(); byte[] first = new byte[1]; await stream.ReadExactlyAsync(first, deadline.Token);
                if (first[0] == 0xFE)
                {
                    Interlocked.Increment(ref _requests);
                    string status = "Legacy fixture§0§8"; byte[] body = Encoding.BigEndianUnicode.GetBytes(status);
                    await stream.WriteAsync(new byte[] { 0xFF, 0, (byte)status.Length }.Concat(body).ToArray(), deadline.Token);
                }
                else
                {
                    if (_legacyOnly) return;
                    byte[] handshake = await ReadPacketAsync(stream, first[0], deadline.Token);
                    await stream.ReadExactlyAsync(first, deadline.Token);
                    byte[] request = await ReadPacketAsync(stream, first[0], deadline.Token);
                    if (handshake.Length < 3 || handshake[0] != 0 || handshake[^1] != 1 || !request.AsSpan().SequenceEqual(new byte[] { 0 })) return;
                    Interlocked.Increment(ref _requests);
                    byte[] json = Encoding.UTF8.GetBytes("{\"version\":{\"name\":\"26.2\",\"protocol\":-1},\"description\":\"Fixture\"}");
                    await stream.WriteAsync(new byte[] { (byte)(json.Length + 2), 0, (byte)json.Length }.Concat(json).ToArray(), deadline.Token);
                }
                // Drain any trailing legacy request bytes before close, avoiding a TCP reset with unread input.
                await stream.CopyToAsync(Stream.Null, deadline.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { }
        }
    }
    private static async Task<byte[]> ReadPacketAsync(Stream stream, byte first, CancellationToken token)
    {
        int size = first & 127, shift = 7; byte[] next = new byte[1];
        while ((first & 128) != 0)
        {
            if (shift >= 28) throw new IOException("Invalid fixture frame length");
            await stream.ReadExactlyAsync(next, token); first = next[0]; size |= (first & 127) << shift; shift += 7;
        }
        if (size is < 1 or > 4096) throw new IOException("Fixture frame too large");
        byte[] packet = new byte[size]; await stream.ReadExactlyAsync(packet, token); return packet;
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; }
}
