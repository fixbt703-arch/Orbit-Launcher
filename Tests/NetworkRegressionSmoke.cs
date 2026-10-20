using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using CmlLib.Core;
using CmlLib.Core.Files;
using OrbitLauncher.Services;

internal static class NetworkRegressionSmoke
{
    public static async Task<int> RunAsync(string root)
    {
        var count = 0;
        async Task Test(string name, Func<Task> action) { await action(); count++; Console.WriteLine("PASS network: " + name); }
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        HttpResponseMessage Good(string text = "success") => new(HttpStatusCode.OK) { Content = new StringContent(text) };
        await Test("a stalled handshake retries via compatibility transport with HTTP/1.1", async () =>
        {
            int primary = 0, compatibility = 0;
            using var http = new HttpClient(new NetworkRecoveryHandler(
                new Handler(async (request, token) => { primary++; Require(request.Version == HttpVersion.Version11, "not HTTP/1.1"); await Task.Delay(10000, token); return Good(); }),
                new Handler((request, _) => { compatibility++; Require(request.VersionPolicy == HttpVersionPolicy.RequestVersionExact, "HTTP upgrade permitted"); return Task.FromResult(Good()); }),
                TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)));
            Require(await http.GetStringAsync("https://fixture.invalid/") == "success" && primary == 1 && compatibility == 1, "fallback failed");
        });
        await Test("temporary HTTP errors retry but login POST is never replayed or disposed early", async () =>
        {
            int calls = 0, fallback = 0;
            using var http = new HttpClient(new NetworkRecoveryHandler(
                new Handler(async (request, _) => { calls++; if (request.Method == HttpMethod.Post) Require(await request.Content!.ReadAsStringAsync() == "login", "POST content changed"); return new(HttpStatusCode.ServiceUnavailable); }),
                new Handler((_, _) => { fallback++; return Task.FromResult(Good()); })));
            Require(await http.GetStringAsync("https://fixture.invalid/") == "success", "503 retry failed");
            using var body = new StringContent("login"); using var response = await http.PostAsync("https://fixture.invalid/login", body);
            Require(response.StatusCode == HttpStatusCode.ServiceUnavailable && calls == 2 && fallback == 1 && await body.ReadAsStringAsync() == "login", "login replayed or body disposed");
        });
        await Test("a stalled metadata body retries using a fresh compatible connection", async () =>
        {
            int primary = 0, fallback = 0;
            using var http = new HttpClient(new NetworkRecoveryHandler(
                new Handler((_, _) => { primary++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }); }),
                new Handler((_, _) => { fallback++; return Task.FromResult(Good("{\"ok\":true}")); }), idleTimeout: TimeSpan.FromMilliseconds(50)));
            var bytes = await LauncherNetwork.GetMetadataAsync(http, "https://fixture.invalid/versions");
            Require(Encoding.UTF8.GetString(bytes) == "{\"ok\":true}" && primary == 1 && fallback == 1, "body stall didn't recover");
        });
        await Test("user cancellation prevents compatibility retry", async () =>
        {
            int fallback = 0;
            using var http = new HttpClient(new NetworkRecoveryHandler(
                new Handler(async (_, token) => { await Task.Delay(10000, token); return Good(); }),
                new Handler((_, _) => { fallback++; return Task.FromResult(Good()); })));
            using var cancellation = new CancellationTokenSource(50);
            try { await http.GetAsync("https://fixture.invalid/", cancellation.Token); throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { }
            Require(fallback == 0, "cancel triggered another request");
        });
        var payload = Encoding.UTF8.GetBytes("verified Minecraft component");
        var sha1 = Convert.ToHexString(SHA1.HashData(payload));
        GameFile FileAt(string path) => new("fixture") { Path = path, Url = "https://fixture.invalid/file", Size = payload.Length, Hash = sha1 };
        await Test("Minecraft partial download retries with TLS fallback before atomic replacement", async () =>
        {
            var path = Path.Combine(root, "network-download", "component.jar"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original");
            int calls = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                calls++; if (calls > 1) Require(request.Options.TryGetValue(LauncherNetwork.CompatibilityTransportKey, out var compatible) && compatible, "body retry didn't select compatibility");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(calls == 1 ? payload[..3] : payload) });
            }));
            var installer = new ReliableGameInstaller(http);
            await installer.Install([FileAt(path)], null, null, CancellationToken.None);
            Require(calls == 2 && File.ReadAllBytes(path).SequenceEqual(payload) && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "partial file committed or not cleaned");
        });
        await Test("repeated bad hashes retain installed files and leave no temporary files", async () =>
        {
            var path = Path.Combine(root, "network-bad-hash", "component.jar"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original");
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[payload.Length]) })));
            try { await new ReliableGameInstaller(http).Install([FileAt(path)], null, null, CancellationToken.None); throw new Exception("invalid component accepted"); }
            catch (IOException) { }
            Require(File.ReadAllText(path) == "original" && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "installed file damaged");
        });
        await Test("Minecraft downloads share launcher transport and use four workers", async () =>
        {
            int requests = 0;
            using var http = new HttpClient(new Handler((_, _) => { requests++; return Task.FromResult(Good("{\"latest\":{\"release\":\"26.2\",\"snapshot\":\"26.2\"},\"versions\":[]}")); }));
            var service = new MinecraftService(http);
            var launcher = (MinecraftLauncher)typeof(MinecraftService).GetMethod("CreateLauncher", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, [new MinecraftPath(Path.Combine(root, "network-launcher"))])!;
            Require(launcher.GameInstaller is ReliableGameInstaller { MaxDownloader: 4 }, "unbounded download workers");
            await launcher.GetAllVersionsAsync(); Require(requests > 0, "Minecraft bypassed supplied HttpClient");
        });
        return count;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> route) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => route(request, cancellationToken); }
    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(10000, cancellationToken); return 0; }
    }
}
