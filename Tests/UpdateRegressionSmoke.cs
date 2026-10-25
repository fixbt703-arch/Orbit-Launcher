using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class UpdateRegressionSmoke
{
    private const string BaseUrl = "https://github.com/fixbt703-arch/Orbit-Launcher/releases/download/";
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "updates"); Directory.CreateDirectory(root);
        var bytes = Enumerable.Range(0, 70000).Select(i => (byte)i).ToArray(); bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var count = 0;
        async Task Test(string name, Func<Task> action) { await action(); count++; Console.WriteLine("PASS update: " + name); }
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task Fails(Func<Task> action)
        {
            try { await action(); } catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or OperationCanceledException or InvalidOperationException) { return; }
            throw new Exception("Unsafe update was accepted");
        }
        string Release(string tag = "v1.5.0", string? title = null, string? digest = null, bool draft = false, bool prerelease = false,
            string? download = null, bool installer = true, bool sums = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag, name = title ?? "Orbit Launcher 1.5.0", body = "Release fixture", draft, prerelease,
            assets = (installer ? new[] { new { name = GitHubUpdateService.InstallerName, state = "uploaded", size = (long)bytes.Length,
                digest = digest ?? "sha256:" + hash, browser_download_url = download ?? BaseUrl + Uri.EscapeDataString(tag) + "/" + GitHubUpdateService.InstallerName } } : [])
                .Concat(sums ? [new { name = "SHA256SUMS.txt", state = "uploaded", size = 100L, digest = "", browser_download_url = BaseUrl + Uri.EscapeDataString(tag) + "/SHA256SUMS.txt" }] : []).ToArray()
        });
        HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> route) => new(new Handler((request, _) => Task.FromResult(route(request))));
        LauncherUpdate Package(string? checksum = null) => new(new Version(1, 5, 0, 0), "v1.5.0", "Fixture",
            new Uri("https://github.com/fixbt703-arch/Orbit-Launcher/releases/tag/v1.5.0"),
            new Uri(BaseUrl + "v1.5.0/" + GitHubUpdateService.InstallerName), bytes.Length, checksum ?? hash);

        await Test("versions compare numerically and normalize missing zero components", () =>
        {
            foreach (var text in new[] { "1.5", "v1.5.0", "1.5.0.0", "V1.5.0+build.3" })
                Require(GitHubUpdateService.TryParseVersion(text, out var value) && value == new Version(1, 5, 0, 0), "version normalization failed");
            foreach (var text in new[] { "1.5.0-beta", "v1", "latest", "1.2.3.4.5", "1.5.0 command" })
                Require(!GitHubUpdateService.TryParseVersion(text, out _), "invalid version accepted");
            GitHubUpdateService.TryParseVersion("1.10", out var newer); GitHubUpdateService.TryParseVersion("1.9", out var older);
            Require(newer > older, "versions compared as strings"); return Task.CompletedTask;
        });
        await Test("new stable release is selected without sending credentials", async () =>
        {
            using var client = Client(request =>
            {
                Require(request.RequestUri!.AbsolutePath == "/repos/fixbt703-arch/Orbit-Launcher/releases/latest", "wrong release endpoint");
                Require(request.Headers.Authorization is null && !request.Headers.Contains("x-api-key"), "credentials sent");
                Require(request.Headers.UserAgent.Count > 0, "GitHub user agent missing"); return Json(Release());
            });
            var update = await new GitHubUpdateService(client, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4, 1));
            Require(update?.Version == new Version(1, 5, 0, 0) && update.Sha256 == hash, "new release not selected");
        });
        await Test("equal, older, draft and prerelease versions never prompt", async () =>
        {
            foreach (var data in new[] { Release("v1.4.1"), Release("v1.3.9"), Release(draft: true), Release(prerelease: true) })
            {
                using var client = Client(_ => Json(data));
                Require(await new GitHubUpdateService(client, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4, 1, 0)) is null, "non-update offered");
            }
        });
        await Test("existing text tags read an explicit Orbit Launcher release title", async () =>
        {
            foreach (var (tag, title) in new[] { ("BigBugFix", "Orbit Launcher 1.5"), ("BigBugFix", "Orbit Launcher1.5"), ("Update_1.5", "Orbit Launcher1.5") })
            {
                using var client = Client(_ => Json(Release(tag, title)));
                var update = await new GitHubUpdateService(client, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4));
                Require(update?.Tag == tag && update.Version == new Version(1, 5, 0, 0) && update.Sha256 == hash, "existing tag unsupported");
            }
        });
        await Test("checksum fallback reads the exact installer entry and rejects ambiguity", async () =>
        {
            using var client = Client(request => request.RequestUri!.Host == "api.github.com" ? Json(Release(digest: "", sums: true))
                : new(HttpStatusCode.OK) { Content = new StringContent(hash + "  " + GitHubUpdateService.InstallerName + "\n") });
            Require((await new GitHubUpdateService(client, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4)))?.Sha256 == hash, "checksum fallback failed");
            await Fails(() => Task.FromResult(GitHubUpdateService.ReadChecksum(hash + "  other.exe")));
            await Fails(() => Task.FromResult(GitHubUpdateService.ReadChecksum(string.Concat(Enumerable.Repeat(hash + "  " + GitHubUpdateService.InstallerName + "\n", 2)))));
        });
        await Test("missing installer, hash and version are recoverable errors", async () =>
        {
            foreach (var data in new[] { Release(installer: false), Release(digest: ""), Release("Release", "Orbit update") })
            {
                using var client = Client(_ => Json(data)); await Fails(() => new GitHubUpdateService(client, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4)));
            }
        });
        await Test("foreign download URLs and redirects are rejected", async () =>
        {
            using var foreign = Client(_ => Json(Release(download: "https://example.com/installer.exe")));
            await Fails(() => new GitHubUpdateService(foreign, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4)));
            var requests = 0;
            using var redirect = Client(_ => { requests++; var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri("https://example.com/file"); return response; });
            await Fails(() => new GitHubUpdateService(redirect, Path.Combine(root, "redirect")).DownloadAsync(Package()));
            Require(requests == 1, "external redirect was contacted");
        });
        await Test("valid GitHub redirect downloads verified bytes and cleans owned files only", async () =>
        {
            using var client = Client(request =>
            {
                if (request.RequestUri!.Host == "github.com")
                { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/test/installer"); return response; }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            });
            var service = new GitHubUpdateService(client, Path.Combine(root, "valid"));
            var downloaded = await service.DownloadAsync(Package()); Require(File.ReadAllBytes(downloaded).SequenceEqual(bytes), "downloaded bytes changed");
            var sentinel = Path.Combine(root, "keep.exe"); File.WriteAllText(sentinel, "keep"); service.DiscardDownload(sentinel);
            Require(File.Exists(sentinel), "cleanup deleted outside cache"); service.DiscardDownload(downloaded); Require(!File.Exists(downloaded), "cache cleanup failed");
        });
        await Test("hash failure preserves existing files and removes incomplete downloads", async () =>
        {
            var cache = Path.Combine(root, "bad-hash"); Directory.CreateDirectory(cache); var sentinel = Path.Combine(cache, "old.exe"); File.WriteAllText(sentinel, "old");
            using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            await Fails(() => new GitHubUpdateService(client, cache).DownloadAsync(Package(new string('f', 64))));
            Require(Directory.GetFiles(cache).SequenceEqual([sentinel]) && File.ReadAllText(sentinel) == "old", "failed update damaged cache or left a partial file");
        });
        await Test("size mismatch and non-EXE payloads cannot be installed", async () =>
        {
            foreach (var payload in new[] { bytes[..100], Encoding.UTF8.GetBytes("<html>not an installer</html>") })
            {
                var cache = Path.Combine(root, "invalid-" + Guid.NewGuid().ToString("N"));
                using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
                var package = Package() with { Sha256 = Convert.ToHexString(SHA256.HashData(payload)), Size = payload[0] == '<' ? payload.Length : bytes.Length };
                await Fails(() => new GitHubUpdateService(client, cache).DownloadAsync(package));
                Require(!Directory.GetFiles(cache).Any(), "invalid payload survived");
            }
        });
        await Test("cancelling a partial stream leaves no installer or temporary file", async () =>
        {
            var cache = Path.Combine(root, "cancel");
            using var client = Client(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(bytes)) });
            using var cancellation = new CancellationTokenSource(140);
            await Fails(() => new GitHubUpdateService(client, cache).DownloadAsync(Package(), cancellationToken: cancellation.Token));
            Require(!Directory.GetFiles(cache).Any(), "cancelled partial file survived");
        });
        await Test("concurrent downloads use independent files", async () =>
        {
            using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            var service = new GitHubUpdateService(client, Path.Combine(root, "parallel"));
            var paths = await Task.WhenAll(service.DownloadAsync(Package()), service.DownloadAsync(Package()));
            Require(paths.Distinct().Count() == 2 && paths.All(File.Exists), "concurrent downloads collided");
            foreach (var path in paths) service.DiscardDownload(path);
        });
        await Test("installer handoff quotes paths and rejects tampered or wrong-version files", async () =>
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "OrbitLauncher.Tests.exe");
            Require(File.Exists(fixture), "test apphost missing");
            var actualBytes = File.ReadAllBytes(fixture);
            Require(GitHubUpdateService.TryParseVersion(FileVersionInfo.GetVersionInfo(fixture).FileVersion, out var actualVersion), "test apphost has no version");
            var package = Package() with { Size = actualBytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(actualBytes)), Version = actualVersion };
            var executable = Path.Combine(root, "Program Files тест", "OrbitLauncher.exe");
            var start = UpdateInstaller.PrepareStart(package, fixture, executable, 42, false);
            Require(start.UseShellExecute && start.ArgumentList.Contains("/DIR=" + Path.GetDirectoryName(executable))
                && start.ArgumentList.Contains("/ORBITUPDATEPID=42") && start.ArgumentList.Contains("/ORBITUPDATE=1") && start.Arguments == "", "handoff command is unsafe");
            await Fails(() => Task.FromResult(UpdateInstaller.PrepareStart(package with { Sha256 = new string('0', 64) }, fixture, executable, 42, false)));
            await Fails(() => Task.FromResult(UpdateInstaller.PrepareStart(package with { Version = new Version(99, 0, 0, 0) }, fixture, executable, 42, false)));
            await Fails(() => Task.FromResult(UpdateInstaller.PrepareStart(package, fixture, Path.Combine(root, "dotnet.exe"), 42, false)));
        });
        await Test("missing releases and rate limits are not reported as up to date", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
            {
                using var client = Client(_ => new(status)); await Fails(() => new GitHubUpdateService(client, Path.Combine(root, "metadata")).CheckAsync(new Version(1, 4)));
            }
        });
        return count;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => route(request, cancellationToken);
    }
    private sealed class SlowStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(50, cancellationToken); return await base.ReadAsync(buffer[..Math.Min(4096, buffer.Length)], cancellationToken); }
    }
}
