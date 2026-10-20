using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using OrbitLauncher.Models;
using OrbitLauncher.Services.Connect;

internal static class ConnectLifecycleSmoke
{
    public static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var token = timeout.Token;
        var backend = new TcpListener(IPAddress.Loopback, 0); backend.Start();
        var type = typeof(ConnectSession).Assembly.GetType("OrbitLauncher.Services.Connect.PendingGameProxy")!;
        await using var proxy = (IAsyncDisposable)Activator.CreateInstance(type)!;
        int port = (int)type.GetProperty("Port")!.GetValue(proxy)!;
        try
        {
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port, token);
            var clientStream = client.GetStream();
            byte[] message = Encoding.UTF8.GetBytes("pending connection and half close");
            await clientStream.WriteAsync(message, token); client.Client.Shutdown(SocketShutdown.Send);
            type.GetMethod("SetTarget")!.Invoke(proxy, [((IPEndPoint)backend.LocalEndpoint).Port]);
            using var server = await backend.AcceptTcpClientAsync(token);
            var serverStream = server.GetStream();
            using var received = new MemoryStream(); await serverStream.CopyToAsync(received, token);
            if (!message.SequenceEqual(received.ToArray())) throw new Exception("Pending proxy lost stream data");
            await serverStream.WriteAsync(message, token); server.Client.Shutdown(SocketShutdown.Send);
            using var reply = new MemoryStream(); await clientStream.CopyToAsync(reply, token);
            if (!message.SequenceEqual(reply.ToArray())) throw new Exception("Proxy lost half-closed response");
        }
        finally { backend.Stop(); }
        Console.WriteLine("PASS Connect pending proxy: queued bytes, half-close request and final response intact");
        await using (var fragmented = new MinecraftStatusFixture())
        using (var client = new TcpClient { NoDelay = true })
        {
            await client.ConnectAsync(IPAddress.Loopback, fragmented.Port, token);
            var stream = client.GetStream(); byte[] first = new byte[1];
            await stream.WriteAsync(new byte[] { 15 }, token);
            var response = stream.ReadAsync(first, token).AsTask();
            await Task.Delay(100, token);
            if (response.IsCompleted) throw new Exception("Minecraft fixture replied before its fragmented handshake and status request arrived");
            byte[] handshake = new byte[] { 0, 47, 9 }.Concat(Encoding.ASCII.GetBytes("localhost"))
                .Concat(new byte[] { (byte)(fragmented.Port >> 8), (byte)fragmented.Port, 1, 1, 0 }).ToArray();
            await stream.WriteAsync(handshake, token);
            if (await response != 1 || first[0] < 3) throw new Exception("Fragmented fixture response missing");
            byte[] packet = new byte[first[0]]; await stream.ReadExactlyAsync(packet, token);
            if (packet[0] != 0) throw new Exception("Fragmented fixture status invalid");
        }
        Console.WriteLine("PASS Minecraft status fixture waits for complete fragmented request before replying");
        await using (var legacy = new MinecraftStatusFixture(legacyOnly: true)) await ConnectMinecraftProbe.QueryAsync(legacy.Port, token);
        Console.WriteLine("PASS Connect game probe supports legacy pre-Netty LAN status without changing game protocol");
        await using var session = new ConnectSession(new GameInstance());
        await session.HandleAsync("attach", "{\"pid\":" + Environment.ProcessId + "}");
        using var invalid = new TcpListener(IPAddress.Loopback, 0); invalid.Start();
        await session.HandleAsync("host", "{\"port\":" + ((IPEndPoint)invalid.LocalEndpoint).Port + "}");
        string busy = await session.HandleAsync("join", System.Text.Json.JsonSerializer.Serialize(new { code = ConnectInvitation.Create(25565, "Other world", "26.2").Encode() }));
        if (!busy.Contains("\"Phase\":\"error\"")) throw new Exception("Busy session silently accepted a different room");
        using var accepted = await invalid.AcceptTcpClientAsync(token);
        await accepted.GetStream().WriteAsync(new byte[] { 2, 0, 0 }, token); accepted.Close();
        while (session.Status.Phase == "starting") await Task.Delay(50, token);
        if (session.Status.Phase != "error") throw new Exception("Invalid game endpoint stuck in starting");
        await session.StopAsync();
        if (session.Status.Phase != "idle") throw new Exception("Stop did not reset failed session");
        Console.WriteLine("PASS Connect lifecycle: invalid game response gives recoverable error, stop resets session");
    }
}
