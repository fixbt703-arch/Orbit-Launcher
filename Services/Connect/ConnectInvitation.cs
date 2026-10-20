using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrbitLauncher.Services.Connect;

public sealed record ConnectInvitation(string Key, int Port, string Name, string MinecraftVersion, string TerracottaCode = "")
{
    public string NetworkName => "orbit-" + Convert.ToHexString(SHA256.HashData(Convert.FromHexString(Key))).ToLowerInvariant()[..24];
    public string Encode()
    {
        var fields = new Dictionary<string, object> { ["v"] = 1, ["key"] = Key, ["port"] = Port, ["name"] = Name, ["mc"] = MinecraftVersion };
        if (TerracottaCode.Length > 0) fields["tc"] = TerracottaCode;
        return "orbit1:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(fields)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public override string ToString() => "Orbit Connect invitation [redacted]";

    public static ConnectInvitation Create(int port, string name, string minecraftVersion) =>
        Parse(new ConnectInvitation(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), port,
            name.Length > 80 ? name[..80] : name, minecraftVersion).Encode());

    public static ConnectInvitation Parse(string text)
    {
        try
        {
            if (text is null || Encoding.UTF8.GetByteCount(text) > 4096) throw new FormatException();
            text = text.Trim();
            if (!text.StartsWith("orbit1:", StringComparison.Ordinal)) throw new FormatException();
            string encoded = text[7..];
            if (!Regex.IsMatch(encoded, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)) throw new FormatException();
            encoded = encoded.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(encoded), new JsonDocumentOptions { MaxDepth = 3 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException();
            var fields = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (fields.Length is < 5 or > 6 || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length
                || fields.Except(new[] { "v", "key", "port", "name", "mc", "tc" }).Any()) throw new FormatException();
            if (root.GetProperty("v").GetInt32() != 1) throw new FormatException();
            string key = root.GetProperty("key").GetString() ?? "";
            int port = root.GetProperty("port").GetInt32();
            string name = root.GetProperty("name").GetString() ?? "";
            string mc = root.GetProperty("mc").GetString() ?? "";
            if (!Regex.IsMatch(key, "^[0-9a-f]{64}$") || port is < 1 or > 65535
                || name.Length is < 1 or > 80 || name.Any(char.IsControl)
                || mc.Length is < 1 or > 64 || !Regex.IsMatch(mc, "^[A-Za-z0-9_.+ -]+$")) throw new FormatException();
            string tc = root.TryGetProperty("tc", out var backup) ? backup.GetString() ?? "" : "";
            if (tc.Length > 0 && !TerracottaTransport.IsRoomCode(tc)) throw new FormatException();
            return new(key, port, name, mc, tc);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or OverflowException or FormatException)
        { throw new FormatException("Некорректное приглашение Orbit Connect. Скопируйте код целиком."); }
    }
}
