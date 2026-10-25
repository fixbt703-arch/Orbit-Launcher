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
            var offline = LicensedSkinBridge.Arguments(path, "11223344556647889900aabbccddeeff", "OrbitViewer", includeViewer: true).SelectMany(argument => argument.Values).ToArray();
            Require(offline.Length==4&&offline[3]=="-Dorbit.skinbridge.includeViewer=true", "offline viewer excluded from Ely fallback");
            Require(!arguments.Any(argument=>argument.Contains("includeViewer")), "licensed viewer's own skin no longer protected by default");
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
            await File.WriteAllTextAsync(Path.Combine(instance.ModsPath, "broken.jar"), "not a zip");
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(instance.ModsPath, "renamed.jar"), System.IO.Compression.ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry("customskinloader/CustomSkinLoader.class").Open()) entry.WriteByte(1);
            foreach (var loader in new[] { "Forge", "NeoForge", "Quilt", "Fabric" })
            { instance.Loader = loader; Require(LicensedSkinBridge.HasCustomSkinLoader(instance), "renamed CustomSkinLoader not detected for " + loader); }
        });
        await Test("invalid skin dimensions fail refresh and retain the last working preview", async () =>
        {
            var bytes = Skin(64, 64, 37);
            using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
            var skins = new ElySkinService(http, Path.Combine(directory, "invalid-dimensions"));
            var previous = await skins.RefreshHeadAsync("OrbitTest");
            Require(previous is not null, "valid preview missing");
            bytes = Skin(8, 8, 93);
            try { await skins.RefreshHeadAsync("OrbitTest"); throw new Exception("invalid PNG dimensions reported as a successful refresh"); }
            catch (InvalidDataException) { }
            Require(ReferenceEquals(previous, skins.GetCachedHead("OrbitTest")), "invalid response replaced working preview");
        });
        await Test("wrong 2FA input is rejected locally and a wrong code retains the previous identity", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler(async request =>
            {
                calls++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                return calls == 1 ? Json(Session(body.RootElement.GetProperty("clientToken").GetString()!))
                    : Json(new { errorMessage = "Invalid credentials. Invalid email or password." }, HttpStatusCode.Unauthorized);
            }));
            var path = Path.Combine(directory, "wrong-code.dat"); var accounts = new ElyAccountService(http, path);
            await accounts.LoginAsync("OrbitTest", "fixture-password"); var previous = accounts.Account; var saved = await File.ReadAllBytesAsync(path);
            foreach (var code in new[] { "12345", "1234567", "abcdef", "１２３４５６" })
            { try { await accounts.LoginAsync("OrbitOther", "fixture-password", code); throw new Exception("invalid code accepted"); } catch (InvalidOperationException) { } }
            Require(calls == 1, "invalid code reached the network");
            try { await accounts.LoginAsync("OrbitOther", "fixture-password", "000000"); throw new Exception("wrong code accepted"); } catch (InvalidOperationException) { }
            Require(ReferenceEquals(previous, accounts.Account) && saved.SequenceEqual(await File.ReadAllBytesAsync(path)), "failed login replaced an existing identity");
        });
        await Test("a different refresh profile is rejected without changing the saved account", async () =>
        {
            using var http = new HttpClient(new Handler(async request =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var client = body.RootElement.GetProperty("clientToken").GetString()!;
                return request.RequestUri!.AbsolutePath.EndsWith("refresh")
                    ? Json(new { accessToken = "other-access", clientToken = client, selectedProfile = new { id = "11223344556647889900aabbccddeeff", name = "OrbitOther" } }) : Json(Session(client));
            }));
            var path = Path.Combine(directory, "wrong-profile.dat"); var accounts = new ElyAccountService(http, path);
            await accounts.LoginAsync("OrbitTest", "fixture-password"); var previous = accounts.Account; var saved = await File.ReadAllBytesAsync(path);
            try { await accounts.GetRequiredSessionAsync(); throw new Exception("different profile silently accepted"); } catch (InvalidDataException) { }
            Require(ReferenceEquals(previous, accounts.Account) && saved.SequenceEqual(await File.ReadAllBytesAsync(path)), "mismatched refresh damaged the previous account");
        });
        await Test("rate limits, malformed refresh and cancellation preserve a stored session", async () =>
        {
            foreach (var scenario in new[] { "rate-limit", "malformed", "transport", "cancelled" })
            {
                using var http = new HttpClient(new Handler(async request =>
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    if (request.RequestUri!.AbsolutePath.EndsWith("authenticate")) return Json(Session(body.RootElement.GetProperty("clientToken").GetString()!));
                    return scenario switch { "rate-limit" => Json(new { errorMessage = "Too many requests" }, HttpStatusCode.TooManyRequests),
                        "malformed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{broken") },
                        "transport" => throw new HttpRequestException("fixture network failure"), _ => throw new OperationCanceledException() };
                }));
                var path = Path.Combine(directory, "preserved-" + scenario + ".dat"); var accounts = new ElyAccountService(http, path);
                await accounts.LoginAsync("OrbitTest", "fixture-password"); var previous = accounts.Account; var saved = await File.ReadAllBytesAsync(path);
                Exception? failure = null; try { await accounts.GetRequiredSessionAsync(); } catch (Exception ex) { failure = ex; }
                Require(failure is not null && failure is not ElySessionExpiredException, "transient failure treated as session expiry");
                Require(ReferenceEquals(previous, accounts.Account) && saved.SequenceEqual(await File.ReadAllBytesAsync(path)), "transient failure erased saved identity");
            }
        });
        await Test("both Minecraft skin layouts decode into a frozen head and stay isolated by name", async () =>
        {
            using var http = new HttpClient(new Handler(request =>
            {
                Require(request.Method == HttpMethod.Get && request.Headers.Authorization is null && request.RequestUri!.Query == "?version=2", "skin request used credentials or the wrong API version");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, request.RequestUri!.AbsolutePath.Contains("OrbitOther") ? 32 : 64, request.RequestUri!.AbsolutePath.Contains("OrbitOther") ? (byte)93 : (byte)37)) });
            }));
            var path = Path.Combine(directory, "isolated-heads"); var skins = new ElySkinService(http, path);
            var first = await skins.RefreshHeadAsync("OrbitTest"); var second = await skins.RefreshHeadAsync("OrbitOther");
            Require(first?.Image.PixelWidth == 8 && first.Image.PixelHeight == 8 && first.Image.IsFrozen && second?.Image.IsFrozen == true, "head layout or cross-thread ownership invalid");
            Require(Red(first!) == 37 && Red(second!) == 93 && Red(skins.GetCachedHead("orbittest")!) == 37, "account heads were mixed");
            var restored = new ElySkinService(http, path);
            Require(Red(restored.GetCachedHead("OrbitTest")!) == 37 && Red(restored.GetCachedHead("OrbitOther")!) == 93, "disk caches were mixed");
        });
        await Test("missing public skins clear only that account's memory and disk cache", async () =>
        {
            foreach (var missing in new[] { HttpStatusCode.NotFound, HttpStatusCode.NoContent })
            {
                var status = HttpStatusCode.OK;
                using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Skin(64, 64, 37)) })));
                var path = Path.Combine(directory, "missing-head-" + missing); var skins = new ElySkinService(http, path);
                await skins.RefreshHeadAsync("OrbitTest"); await skins.RefreshHeadAsync("OrbitOther"); status = missing;
                Require(await skins.RefreshHeadAsync("OrbitTest") is null && skins.GetCachedHead("OrbitTest") is null && skins.GetCachedHead("OrbitOther") is not null, "missing response did not isolate account cleanup");
                var restored = new ElySkinService(http, path);
                Require(restored.GetCachedHead("OrbitTest") is null && restored.GetCachedHead("OrbitOther") is not null, "removed head reappeared from disk");
            }
        });
        await Test("outages, oversized downloads and malformed PNG retain the last skin", async () =>
        {
            foreach (var scenario in new[] { "outage", "oversized", "malformed" })
            {
                var first = true;
                using var http = new HttpClient(new Handler(_ => Task.FromResult(first
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 37)) }
                    : new HttpResponseMessage(scenario == "outage" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new ByteArrayContent(scenario == "oversized" ? new byte[196609] : "not a PNG"u8.ToArray()) })));
                var skins = new ElySkinService(http, Path.Combine(directory, "failed-head-" + scenario)); var previous = await skins.RefreshHeadAsync("OrbitTest"); first = false;
                Exception? failure = null; try { await skins.RefreshHeadAsync("OrbitTest"); } catch (Exception ex) { failure = ex; }
                Require(failure is not null && ReferenceEquals(previous, skins.GetCachedHead("OrbitTest")), "failed response replaced a working preview or reported success");
            }
        });
        await Test("skin redirects upgrade official HTTP storage and reject other hosts before requesting them", async () =>
        {
            foreach (var target in new[] { "http://ely.by/storage/skins/fixture.png", "/storage/fixture.png", "https://evil.test/skin.png", "https://user@ely.by/skin.png", "https://ely.by:8443/skin.png", "file:///C:/skin.png" })
            {
                var calls = 0;
                using var http = new HttpClient(new Handler(request =>
                {
                    if (++calls == 1) { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(target, UriKind.RelativeOrAbsolute); return Task.FromResult(response); }
                    Require(request.RequestUri!.Scheme == "https" && request.RequestUri.IsDefaultPort && (request.RequestUri.Host == "ely.by" || request.RequestUri.Host == "skinsystem.ely.by"), "untrusted redirect reached transport");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 37)) });
                }));
                var skins = new ElySkinService(http, Path.Combine(directory, "redirect-" + Guid.NewGuid().ToString("N")));
                bool allowed = target == "http://ely.by/storage/skins/fixture.png" || target == "/storage/fixture.png";
                try { var head = await skins.RefreshHeadAsync("OrbitTest"); Require(allowed && head is not null && calls == 2, "redirect response not decoded"); }
                catch (InvalidDataException) { Require(!allowed && calls == 1, "official redirect blocked or untrusted host requested"); }
            }
        });
        await Test("refreshes finishing after a different account cannot overwrite its head", async () =>
        {
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.Contains("OrbitTest") ? pending.Task
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 93)) })));
            var skins = new ElySkinService(http, Path.Combine(directory, "account-race"));
            var old = skins.RefreshHeadAsync("OrbitTest"); var current = await skins.RefreshHeadAsync("OrbitOther");
            pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 37)) }); await old;
            Require(Red(skins.GetCachedHead("OrbitOther")!) == 93 && ReferenceEquals(current, skins.GetCachedHead("OrbitOther")), "late previous-account response replaced the current head");
        });
        await Test("older refresh responses cannot replace or erase a newer preview", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NoContent })
            {
                var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
                using var http = new HttpClient(new Handler(_ => ++calls == 1 ? pending.Task
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 93)) })));
                var skins = new ElySkinService(http, Path.Combine(directory, "stale-head-" + status));
                var stale = skins.RefreshHeadAsync("OrbitTest"); var current = await skins.RefreshHeadAsync("orbittest");
                pending.SetResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Skin(64, 64, 37)) });
                var returned = await stale;
                Require(ReferenceEquals(current, returned) && ReferenceEquals(current, skins.GetCachedHead("OrbitTest")), "older " + status + " response replaced or erased the newer preview");
            }
        });
        await Test("the latest failed refresh retains the last working head and suppresses older responses", async () =>
        {
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            using var http = new HttpClient(new Handler(_ => ++calls switch {
                1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 93)) }),
                2 => pending.Task,
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) }));
            var skins = new ElySkinService(http, Path.Combine(directory, "latest-refresh-fails")); var current = await skins.RefreshHeadAsync("OrbitTest");
            var stale = skins.RefreshHeadAsync("OrbitTest");
            try { await skins.RefreshHeadAsync("OrbitTest"); throw new Exception("latest failed refresh reported success"); } catch (HttpRequestException) { }
            Require(ReferenceEquals(current, skins.GetCachedHead("OrbitTest")), "latest failure removed the last working head");
            pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Skin(64, 64, 37)) });
            Require(ReferenceEquals(current, await stale) && ReferenceEquals(current, skins.GetCachedHead("OrbitTest")), "older response replaced the last working head after latest failure");
        });
        return passed;
    }
    private static byte Red(PlayerHeadImage head) { var pixel = new byte[4]; head.Image.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0); return pixel[2]; }
    private static byte[] Skin(int width, int height, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 8; y < Math.Min(16, height); y++) for (int x = 8; x < Math.Min(16, width); x++)
        { var offset = (y * width + x) * 4; pixels[offset + 2] = red; pixels[offset + 3] = 255; }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request);
    }
}
