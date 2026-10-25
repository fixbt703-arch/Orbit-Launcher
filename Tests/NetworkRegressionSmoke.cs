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
        await Test("default handshake fallback fits inside a skin request deadline", async () =>
        {
            using var http = new HttpClient(new NetworkRecoveryHandler(
                new Handler(async (_, token) => { await Task.Delay(30000, token); return Good(); }),
                new Handler((_, _) => Task.FromResult(Good()))));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            Require(await http.GetStringAsync("https://fixture.invalid/skin", deadline.Token) == "success", "skin deadline prevented fallback");
        });
        await Test("HTTPS through an HTTP proxy preserves the native TLS ClientHello", async () =>
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var previousProxy = HttpClient.DefaultProxy;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                HttpClient.DefaultProxy = new WebProxy(new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port));
                var capture = Task.Run(async () =>
                {
                    using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                    await using var wire = socket.GetStream();
                    var request = new List<byte>(); var one = new byte[1];
                    while (request.Count < 8192)
                    {
                        await wire.ReadExactlyAsync(one, deadline.Token); request.Add(one[0]);
                        if (request.Count >= 4 && request.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                    }
                    Require(Encoding.ASCII.GetString(request.ToArray()).StartsWith("CONNECT orbit-alt9.invalid:443 "), "proxy CONNECT changed");
                    await wire.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), deadline.Token);
                    var hello = new byte[65536]; await wire.ReadExactlyAsync(hello.AsMemory(0, 5), deadline.Token);
                    int length = hello[3] * 256 + hello[4];
                    await wire.ReadExactlyAsync(hello.AsMemory(5, length), deadline.Token);
                    Require(length == 4 + hello[6] * 65536 + hello[7] * 256 + hello[8]
                        && Encoding.ASCII.GetString(hello, 0, length + 5).Contains("orbit-alt9.invalid"), "TLS record or SNI was damaged");
                    deadline.Cancel();
                });
                using var http = LauncherNetwork.CreateClient();
                try { await http.GetAsync("https://orbit-alt9.invalid/", deadline.Token); }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                await capture;
            }
            finally { HttpClient.DefaultProxy = previousProxy; }
        });
        await Test("a slow login keeps its original deadline and is sent exactly once", async () =>
        {
            int calls = 0, fallback = 0;
            using var http = new HttpClient(new NetworkRecoveryHandler(
                new Handler(async (request, token) => { calls++; await Task.Delay(4500, token); return Good(await request.Content!.ReadAsStringAsync(token)); }),
                new Handler((_, _) => { fallback++; return Task.FromResult(Good()); })));
            using var body = new StringContent("fixture-login");
            using var response = await http.PostAsync("https://fixture.invalid/login", body);
            Require(await response.Content.ReadAsStringAsync() == "fixture-login" && calls == 1 && fallback == 0 && await body.ReadAsStringAsync() == "fixture-login", "slow login cancelled, changed or replayed");
        });
        await Test("the compatible connection still rejects an untrusted TLS certificate", async () =>
        {
            using var key = RSA.Create(2048);
            var certificateRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
            certificateRequest.CertificateExtensions.Add(names.Build());
            using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                generated.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), "",
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet);
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                using var tls = new System.Net.Security.SslStream(socket.GetStream());
                try { await tls.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions { ServerCertificate = certificate }, deadline.Token); }
                catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException) { }
            });
            using var http = LauncherNetwork.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/login") { Content = new StringContent("fixture-login") };
            request.Options.Set(LauncherNetwork.CompatibilityTransportKey, true);
            try { using var response = await http.SendAsync(request, deadline.Token); throw new Exception("untrusted TLS certificate accepted"); }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException) { }
            await server;
        });
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
