using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OrbitLauncher.Services.Connect;

public static class ConnectMinecraftProbe
{
    public static async Task<double> QueryAsync(int port, CancellationToken cancellationToken)
    {
        try { return await QueryModernAsync(port, cancellationToken); }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is IOException or SocketException or JsonException or OperationCanceledException)
        { return await QueryLegacyAsync(port, cancellationToken); }
    }
    private static async Task<double> QueryLegacyAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var token = timeout.Token; var watch = Stopwatch.StartNew();
        using var client = new TcpClient { NoDelay = true }; await client.ConnectAsync("127.0.0.1", port, token);
        var stream = client.GetStream(); await stream.WriteAsync(new byte[] { 0xFE, 0x01 }, token);
        byte[] header = new byte[3]; await stream.ReadExactlyAsync(header, token);
        int length = (header[1] << 8) | header[2];
        if (header[0] != 0xFF || length is < 5 or > 4096) throw new IOException("Игровой сервер не ответил.");
        byte[] bytes = new byte[length * 2]; await stream.ReadExactlyAsync(bytes, token);
        string text = Encoding.BigEndianUnicode.GetString(bytes);
        var parts = text.Split(text.Contains('\0') ? '\0' : '§');
        if (parts.Length < 3 || !int.TryParse(parts[^2], out int players) || !int.TryParse(parts[^1], out int maximum) || players < 0 || maximum < 0)
            throw new IOException("Это не игровой сервер Minecraft.");
        return watch.Elapsed.TotalMilliseconds;
    }
    private static async Task<double> QueryModernAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var token = timeout.Token; var watch = Stopwatch.StartNew();
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync("127.0.0.1", port, token);
        using var payload = new MemoryStream(); payload.WriteByte(0); WriteVarInt(payload, -1);
        byte[] address = Encoding.UTF8.GetBytes("localhost"); WriteVarInt(payload, address.Length); payload.Write(address);
        payload.WriteByte((byte)(port >> 8)); payload.WriteByte((byte)port); payload.WriteByte(1);
        using var packet = new MemoryStream(); WriteVarInt(packet, (int)payload.Length); payload.Position = 0; payload.CopyTo(packet);
        packet.WriteByte(1); packet.WriteByte(0);
        var stream = client.GetStream(); await stream.WriteAsync(packet.ToArray(), token);
        int size = await ReadVarInt(stream, token);
        if (size is < 3 or > 1024 * 1024 || await ReadVarInt(stream, token) != 0) throw new IOException("Игровой сервер не ответил.");
        int textLength = await ReadVarInt(stream, token);
        if (textLength <= 0 || textLength > size - 2) throw new IOException("Некорректный ответ игрового сервера.");
        byte[] bytes = new byte[textLength]; await stream.ReadExactlyAsync(bytes, token);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        if (!json.RootElement.TryGetProperty("version", out _) || !json.RootElement.TryGetProperty("description", out _)) throw new IOException("Это не игровой сервер Minecraft.");
        return watch.Elapsed.TotalMilliseconds;
    }
    private static void WriteVarInt(Stream stream, int value)
    { uint rest = (uint)value; do { byte part = (byte)(rest & 127); rest >>= 7; stream.WriteByte(rest == 0 ? part : (byte)(part | 128)); } while (rest != 0); }
    private static async Task<int> ReadVarInt(Stream stream, CancellationToken token)
    {
        int value = 0; byte[] one = new byte[1];
        for (int i = 0; i < 5; i++) { await stream.ReadExactlyAsync(one, token); value |= (one[0] & 127) << (7 * i); if ((one[0] & 128) == 0) return value; }
        throw new IOException("Некорректная длина игрового ответа.");
    }
}
