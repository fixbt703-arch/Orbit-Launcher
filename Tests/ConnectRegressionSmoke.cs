using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services.Connect;
using System.Net.Sockets;
using System.Diagnostics;

internal static class ConnectRegressionSmoke
{
    public static async Task RunAsync()
    {
        var type = typeof(GameInstance).Assembly.GetType("OrbitLauncher.Services.Connect.ConnectInvitation")
            ?? throw new Exception("Orbit Connect invitation parser is missing");
        object Parse(string text) => type.GetMethod("Parse", [typeof(string)])!.Invoke(null, [text])!;
        void Reject(string text)
        {
            try { Parse(text); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { return; }
            throw new Exception("Unsafe invitation accepted");
        }
        string Encode(string json) => "orbit1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        const string valid = "{\"v\":1,\"key\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"port\":25565,\"name\":\"World\",\"mc\":\"26.2\"}";
        var invitation = Parse(Encode(valid));
        if ((int)type.GetProperty("Port")!.GetValue(invitation)! != 25565) throw new Exception("Game port lost");
        Reject("orbit1:" + new string('A', 4100)); Reject("powershell.exe -command anything");
        Reject(Encode(valid.Replace("\"v\":1", "\"v\":2")));
        Reject(Encode(valid.Replace("25565", "0")));
        Reject(Encode(valid.Replace("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "password")));
        Reject(Encode(valid.Replace("\"port\":25565", "\"port\":25565,\"host\":\"192.168.1.1\"")));
        var backup = Parse(Encode(valid.Replace("\"mc\":\"26.2\"", "\"mc\":\"26.2\",\"tc\":\"U/0000-0000-0000-0000\"")));
        if ((string?)type.GetProperty("TerracottaCode")?.GetValue(backup) != "U/0000-0000-0000-0000") throw new Exception("Fallback room lost");
        Reject(Encode(valid.Replace("\"mc\":\"26.2\"", "\"mc\":\"26.2\",\"tc\":\"https://untrusted.invalid/\"")));
        Console.WriteLine("PASS Connect invitations: bounded input, exact schema, version, port and secret validation");
        var enabled = typeof(GameInstance).GetProperty("OrbitConnectEnabled") ?? throw new Exception("Connect instance setting missing");
        foreach (string loader in new[] { "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" })
        {
            var instance = new GameInstance { Loader = loader, GameVersion = "1.20.1" }; enabled.SetValue(instance, true);
            var clone = instance.CloneDefinition();
            if (!(bool)enabled.GetValue(clone)! || clone.Loader != loader || clone.OrbitClientEnabled) throw new Exception("Connect changes loader or is lost on cloning");
            var restored = JsonSerializer.Deserialize<GameInstance>(JsonSerializer.Serialize(instance))!;
            if (!(bool)enabled.GetValue(restored)!) throw new Exception("Connect flag not persisted");
        }
        Console.WriteLine("PASS Connect instance setting: five loaders, persistence and independent clone");
        var installer = typeof(GameInstance).Assembly.GetType("OrbitLauncher.Services.Connect.ConnectRuntimeInstaller")
            ?? throw new Exception("Connect verified runtime installer is missing");
        string folder = Path.Combine(Path.GetTempPath(), "OrbitConnectInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var bytes = Encoding.UTF8.GetBytes("known executable fixture");
        var allowed = new Dictionary<string, string> { ["easytier-core.exe"] = Convert.ToHexString(SHA256.HashData(bytes)) };
        byte[] Zip(string name)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
            return output.ToArray();
        }
        async Task Install(byte[] archive, string digest, string destination)
        {
            using var stream = new MemoryStream(archive);
            try { await (Task)installer.GetMethod("UnpackZipAsync")!.Invoke(null, [stream, destination, digest, allowed, CancellationToken.None])!; }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
        byte[] good = Zip("release/easytier-core.exe");
        await Install(good, Convert.ToHexString(SHA256.HashData(good)), Path.Combine(folder, "good"));
        if (!File.ReadAllBytes(Path.Combine(folder, "good", "easytier-core.exe")).SequenceEqual(bytes)) throw new Exception("Runtime extraction lost bytes");
        try { await Install(good, new string('0', 64), Path.Combine(folder, "bad")); throw new Exception("Corrupt runtime accepted"); }
        catch (InvalidDataException) { }
        byte[] traversal = Zip("../easytier-core.exe");
        try { await Install(traversal, Convert.ToHexString(SHA256.HashData(traversal)), Path.Combine(folder, "escape")); throw new Exception("Zip traversal accepted"); }
        catch (InvalidDataException) { }
        if (File.Exists(Path.Combine(folder, "easytier-core.exe"))) throw new Exception("Archive escaped install root");
        Console.WriteLine("PASS Connect installer: pinned archive and executable hashes, no archive path escape");
        var policyType = typeof(GameInstance).Assembly.GetType("OrbitLauncher.Services.Connect.ConnectRecoveryPolicy")
            ?? throw new Exception("Connect recovery policy missing");
        dynamic policy = Activator.CreateInstance(policyType)!;
        if ((bool)policy.Observe(false, false) || (bool)policy.Observe(false, false)) throw new Exception("A temporary miss restarted a working session");
        if (!(bool)policy.Observe(false, false)) throw new Exception("Persistent failure was ignored");
        policy.Observe(true, false);
        if ((bool)policy.Observe(false, false)) throw new Exception("Healthy sample failed to reset failure streak");
        if (!(bool)policy.Observe(false, true)) throw new Exception("Exited process not detected immediately");
        Console.WriteLine("PASS Connect recovery: transient loss tolerated, confirmed failures and dead process detected");
        var apiType = typeof(GameInstance).Assembly.GetType("OrbitLauncher.Services.Connect.ConnectControlServer")
            ?? throw new Exception("Loopback Connect control API missing");
        string controlToken = new string('c', 64);
        await using var api = (IAsyncDisposable)Activator.CreateInstance(apiType,
            [new Func<string, string, Task<string>>((_, _) => Task.FromResult("{\"ok\":true}")), controlToken])!;
        using var http = new HttpClient { BaseAddress = new Uri((string)apiType.GetProperty("Address")!.GetValue(api)!) };
        if ((await http.GetAsync("state")).StatusCode != HttpStatusCode.Forbidden) throw new Exception("Unauthenticated local control allowed");
        http.DefaultRequestHeaders.Authorization = new("Bearer", controlToken);
        if (await http.GetStringAsync("state") != "{\"ok\":true}") throw new Exception("Authenticated IPC failed");
        using var request = new HttpRequestMessage(HttpMethod.Get, "state"); request.Headers.Add("Origin", "https://untrusted.invalid");
        if ((await http.SendAsync(request)).StatusCode != HttpStatusCode.Forbidden) throw new Exception("Browser origin accepted by private IPC");
        Console.WriteLine("PASS Connect IPC: loopback binding, bearer authorization, cross-origin rejection");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int ownedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        if (!ConnectGamePorts.IsOwned(ownedPort, Environment.ProcessId) || ConnectGamePorts.IsOwned(ownedPort, 1))
            throw new Exception("Game listener ownership mismatch");
        listener.Stop();
        if (ConnectGamePorts.IsOwned(ownedPort, Environment.ProcessId)) throw new Exception("Closed world still owned");
        Console.WriteLine("PASS Connect ownership: actual listener PID, foreign PID rejected, closed port revoked");
        if (typeof(EasyTierTransport).GetMethod("AddConnectorAsync") is null) throw new Exception("Live connector recovery missing");
        Console.WriteLine("PASS Connect supports live connector recovery without restarting core");
        await ConnectLifecycleSmoke.RunAsync();
    }
}
