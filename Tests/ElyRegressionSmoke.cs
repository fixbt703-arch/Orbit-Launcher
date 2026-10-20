using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Controls;
using OrbitLauncher.Services;

internal static class ElyRegressionSmoke
{
    public static async Task<int> RunAsync(string root)
    {
        var passed = 0;
        async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS Ely.by: " + name); }
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        var directory = Path.Combine(root, "ely"); Directory.CreateDirectory(directory);
        var id = "ffc8fdc95824509e8a57c99b940fb996";
        object Session(string client, string access = "fixture-access") => new { accessToken = access, clientToken = client, selectedProfile = new { id, name = "OrbitTest" } };
        HttpResponseMessage Json(object data, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };

        await Test("password is sent only to the official HTTPS POST; token is encrypted and restored", async () =>
        {
            using var http = new HttpClient(new Handler(async request =>
            {
                Require(request.Method == HttpMethod.Post && request.RequestUri!.ToString() == "https://authserver.ely.by/auth/authenticate", "credentials destination");
                Require(request.Headers.Authorization is null && request.RequestUri!.Query == "", "credentials in headers or URL");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Require(body.RootElement.GetProperty("password").GetString() == "fixture-password", "password changed");
                return Json(Session(body.RootElement.GetProperty("clientToken").GetString()!));
            }));
            var path = Path.Combine(directory, "encrypted.dat");
            var service = new ElyAccountService(http, path);
            await service.LoginAsync("OrbitTest", "fixture-password");
            var content = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            Require(!content.Contains("fixture-access") && !content.Contains("fixture-password") && !content.Contains("OrbitTest"), "unencrypted data persisted");
            var restored = new ElyAccountService(http, path); await restored.RestoreAsync();
            Require(restored.Account?.Id == id && restored.Account.Name == "OrbitTest", "restore failed");
        });
        await Test("two-factor challenge retries only when a code is provided", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler(async request =>
            {
                calls++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                if (calls == 1) return Json(new { errorMessage = "Account protected with two factor auth." }, HttpStatusCode.Unauthorized);
                Require(body.RootElement.GetProperty("password").GetString() == "fixture-password:123456", "wrong 2FA format");
                return Json(Session(body.RootElement.GetProperty("clientToken").GetString()!));
            }));
            var path = Path.Combine(directory, "2fa.dat"); var service = new ElyAccountService(http, path);
            try { await service.LoginAsync("OrbitTest", "fixture-password"); throw new Exception("challenge lost"); } catch (ElyTwoFactorRequiredException) { }
            Require(calls == 1 && !File.Exists(path) && service.Account is null, "challenge saved a session or replayed credentials");
            await service.LoginAsync("OrbitTest", "fixture-password", "123456"); Require(service.IsSignedIn, "2FA did not log in");
        });
        await Test("invalid credentials and rate limiting do not create a session", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests })
            {
                using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new { errorMessage = "Invalid credentials" }, status))));
                var path = Path.Combine(directory, status + ".dat"); var service = new ElyAccountService(http, path);
                try { await service.LoginAsync("OrbitTest", "fixture-password"); throw new Exception("bad credentials accepted"); } catch (InvalidOperationException) { }
                Require(!service.IsSignedIn && !File.Exists(path), "invalid session saved");
            }
        });
        await Test("refresh preserves Ely identity and uses the new access token", async () =>
        {
            using var http = new HttpClient(new Handler(async request =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                if (request.RequestUri!.AbsolutePath.EndsWith("refresh"))
                {
                    Require(body.RootElement.GetProperty("accessToken").GetString() == "fixture-access", "wrong token refreshed");
                    return Json(Session(body.RootElement.GetProperty("clientToken").GetString()!, "refreshed-access"));
                }
                return Json(Session(body.RootElement.GetProperty("clientToken").GetString()!));
            }));
            var service = new ElyAccountService(http, Path.Combine(directory, "refresh.dat")); await service.LoginAsync("OrbitTest", "fixture-password");
            var session = await service.GetRequiredSessionAsync();
            Require(session.Username == "OrbitTest" && session.UUID == id && session.AccessToken == "refreshed-access" && session.UserType == "mojang", "game identity differs");
        });
        await Test("expired session is cleared; network errors retain the stored session", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable })
            {
                using var http = new HttpClient(new Handler(async request =>
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    return request.RequestUri!.AbsolutePath.EndsWith("refresh") ? Json(new { errorMessage = "Invalid token" }, status) : Json(Session(body.RootElement.GetProperty("clientToken").GetString()!));
                }));
                var path = Path.Combine(directory, "expiry-" + status + ".dat"); var service = new ElyAccountService(http, path);
                await service.LoginAsync("OrbitTest", "fixture-password");
                try { await service.GetRequiredSessionAsync(); throw new Exception("refresh accepted"); }
                catch (ElySessionExpiredException) { Require(status == HttpStatusCode.Unauthorized, "network failure treated as expiry"); }
                catch (HttpRequestException) { Require(status == HttpStatusCode.ServiceUnavailable, "expiry treated as network failure"); }
                Require(service.IsSignedIn == (status == HttpStatusCode.ServiceUnavailable), "incorrect account cleanup");
                Require(File.Exists(path) == service.IsSignedIn, "incorrect token cleanup");
            }
        });
        await Test("logout invalidates only this session, even when the server is unavailable", async () =>
        {
            var invalidated = false;
            using var http = new HttpClient(new Handler(async request =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                if (request.RequestUri!.AbsolutePath.EndsWith("authenticate")) return Json(Session(body.RootElement.GetProperty("clientToken").GetString()!));
                Require(request.RequestUri.AbsolutePath == "/auth/invalidate", "global signout used"); invalidated = true;
                throw new HttpRequestException("fixture network failure");
            }));
            var path = Path.Combine(directory, "logout.dat"); var service = new ElyAccountService(http, path);
            await service.LoginAsync("OrbitTest", "fixture-password"); await service.SignOutAsync();
            Require(invalidated && !service.IsSignedIn && !File.Exists(path), "logout failed");
        });
        await Test("cancelled login and damaged protected cache cannot produce a session", async () =>
        {
            using var http = new HttpClient(new Handler(_ => throw new Exception("cancelled request reached transport")));
            var path = Path.Combine(directory, "damaged.dat"); var service = new ElyAccountService(http, path);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await service.LoginAsync("OrbitTest", "fixture-password", token: cancellation.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { }
            await File.WriteAllTextAsync(path, "not a protected session"); await service.RestoreAsync(); Require(!service.IsSignedIn, "damaged session loaded");
        });
        await Test("a mismatched agent hash is rejected and the existing file is preserved", async () =>
        {
            var folder = Path.Combine(directory, "agent"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "authlib-injector-" + ElyGameIntegration.AgentVersion + ".jar"); await File.WriteAllTextAsync(path, "old fixture");
            using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("invalid agent"u8.ToArray()) })));
            try { await ElyGameIntegration.EnsureAgentAsync(http, default, folder); throw new Exception("unverified agent executed"); } catch (InvalidDataException) { }
            Require(await File.ReadAllTextAsync(path) == "old fixture" && !Directory.EnumerateFiles(folder, "*.part").Any(), "cache damaged or part leaked");
        });
        await Test("portal accepts only official HTTPS hosts", () =>
        {
            foreach (var url in new[] { "https://ely.by/skins/add", "https://account.ely.by/login", "https://authserver.ely.by/" }) Require(ElySkinsPanel.IsAllowed(url), "official host blocked");
            foreach (var url in new[] { "http://ely.by/", "https://ely.by.evil.test/", "https://evil.test/ely.by", "file:///C:/test", "https://ely.by:8443/", "https://user@ely.by/" }) Require(!ElySkinsPanel.IsAllowed(url), "untrusted navigation allowed");
            return Task.CompletedTask;
        });
        await Test("licensed texture agent is extracted, verified and repaired without a download", async () =>
        {
            var folder = Path.Combine(directory, "licensed agent с пробелом");
            var path = await LicensedSkinBridge.EnsureAsync(default, folder);
            var original = await File.ReadAllBytesAsync(path);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                Require(zip.GetEntry("org/orbitlauncher/skinbridge/Bridge.class") is not null, "bridge payload missing");
                Require(zip.GetEntry("META-INF/LICENSE-ASM.txt") is not null, "dependency license missing");
                Require(!zip.Entries.Any(entry => entry.FullName.StartsWith("org/objectweb/asm/", StringComparison.Ordinal)), "ASM not relocated");
            }
            await File.WriteAllTextAsync(path, "damaged fixture");
            Require(await LicensedSkinBridge.EnsureAsync(default, folder) == path, "changed extraction location");
            Require(original.SequenceEqual(await File.ReadAllBytesAsync(path)), "corrupt cached agent wasn't restored");
            Require(!Directory.EnumerateFiles(folder, "*.part").Any(), "temporary file leaked");
        });
        await Test("licensed launch arguments contain only the agent and public viewer identity", () =>
        {
            var path = Path.Combine(directory, "folder with spaces", "agent.jar");
            var arguments = LicensedSkinBridge.Arguments(path, "11223344-5566-4788-9900-aabbccddeeff", "OrbitViewer").SelectMany(argument => argument.Values).ToArray();
            Require(arguments.Length == 3 && arguments[0] == "-javaagent:" + Path.GetFullPath(path), "agent path was split or altered");
            Require(arguments[1] == "-Dorbit.skinbridge.viewer=11223344556647889900aabbccddeeff", "viewer UUID missing");
            Require(arguments[2] == "-Dorbit.skinbridge.viewerName=OrbitViewer", "viewer name missing");
            Require(!arguments.Any(argument => argument.Contains("authserver") || argument.Contains("account.ely.by") || argument.Contains("accessToken")), "authentication override in licensed launch");
            try { LicensedSkinBridge.Arguments(path, "not-a-uuid", "OrbitViewer"); throw new Exception("invalid viewer accepted"); } catch (InvalidDataException) { }
            try { LicensedSkinBridge.Arguments(path, "11223344556647889900aabbccddeeff", "invalid name"); throw new Exception("invalid name accepted"); } catch (InvalidDataException) { }
            var migrated = JsonSerializer.Deserialize<OrbitLauncher.Models.LauncherSettings>("{\"offlineModeEnabled\":false}");
            Require(migrated!.ShowElySkinsForLicensedAccounts, "existing accounts don't receive the default setting");
            return Task.CompletedTask;
        });
        await Test("an existing CustomSkinLoader is preserved and not combined with a second interceptor", async () =>
        {
            var instance = new OrbitLauncher.Models.GameInstance { Loader = "Fabric", DirectoryPath = Path.Combine(directory, "external-skins") };
            Directory.CreateDirectory(instance.ModsPath);
            var path = Path.Combine(instance.ModsPath, "existing-custom-skins.jar");
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry("customskinloader/bootstrap/Bootstrap.class").Open()) entry.WriteByte(1);
            var original = await File.ReadAllBytesAsync(path);
            Require(LicensedSkinBridge.HasCustomSkinLoader(instance), "existing skin loader not detected");
            Require(original.SequenceEqual(await File.ReadAllBytesAsync(path)), "user mod was modified");
            instance.Loader = "Vanilla";
            Require(!LicensedSkinBridge.HasCustomSkinLoader(instance), "inactive vanilla mods disabled the bridge");
            instance.Loader = "Fabric"; File.Move(path, path + ".disabled");
            Require(!LicensedSkinBridge.HasCustomSkinLoader(instance), "disabled mod disabled the bridge");
        });
        return passed;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request);
    }
}
