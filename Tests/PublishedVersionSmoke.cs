using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Services;

internal static class PublishedVersionSmoke
{
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "published-version");
        var count = 0;
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Test(string name, Func<string, Task> action)
        {
            await action(Path.Combine(root, (++count).ToString()));
            Console.WriteLine("PASS published version: " + name);
        }
        string Release(string tag, string title) => JsonSerializer.Serialize(new
        {
            tag_name = tag, name = title, draft = false, prerelease = false, assets = Array.Empty<object>()
        });
        HttpResponseMessage Content(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        HttpResponseMessage PageRedirect(string tag)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://github.com/fixbt703-arch/Orbit-Launcher/releases/tag/" + tag);
            return response;
        }
        HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> route) => new(new Handler(route));
        async Task CheckFails(GitHubUpdateService service)
        {
            try { await service.CheckAsync(new Version(9, 0)); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException) { return; }
            throw new Exception("Unavailable update check reported success");
        }

        await Test("published version survives older-release comparison and restart", async cache =>
        {
            using var client = Client(_ => Content(Release("Update_1.5", "Orbit Launcher1.5")));
            var service = new GitHubUpdateService(client, cache);
            Require(await service.CheckAsync(new Version(1, 10)) is null, "older release offered");
            Require(service.PublishedVersion == "1.5", "published version replaced by assembly version");
            using var offline = Client(_ => throw new HttpRequestException("offline"));
            var restarted = new GitHubUpdateService(offline, cache);
            Require(restarted.PublishedVersion == "1.5", "published version not cached");
            await CheckFails(restarted);
            Require(restarted.PublishedVersion == "1.5", "offline failure lost cached release");
        });
        await Test("text tags accept the exact compact release title", async cache =>
        {
            using var client = Client(_ => Content(Release("BigBugFix", "Orbit Launcher1.5.0")));
            var service = new GitHubUpdateService(client, cache);
            await service.CheckAsync(new Version(9, 0));
            Require(service.PublishedVersion == "1.5.0", "release title precision lost");
        });
        await Test("unavailable API uses trusted release page for display only", async cache =>
        {
            using var client = Client(request => request.RequestUri!.Host == "api.github.com"
                ? new(HttpStatusCode.Forbidden)
                : request.RequestUri.AbsolutePath.EndsWith("/latest") ? PageRedirect("Update_1.5")
                : Content("<html><title>Release Orbit Launcher1.5 · fixbt703-arch/Orbit-Launcher · GitHub</title></html>"));
            var service = new GitHubUpdateService(client, cache);
            await CheckFails(service);
            Require(service.PublishedVersion == "1.5", "rate-limit page fallback failed");
        });
        await Test("unavailable API reads a title only within the trusted release page", async cache =>
        {
            using var client = Client(request => request.RequestUri!.Host == "api.github.com"
                ? new(HttpStatusCode.ServiceUnavailable)
                : request.RequestUri.AbsolutePath.EndsWith("/latest") ? PageRedirect("BigBugFix")
                : Content("<title>Release Orbit Launcher1.5 · fixbt703-arch/Orbit-Launcher · GitHub</title>"));
            var service = new GitHubUpdateService(client, cache);
            await CheckFails(service);
            Require(service.PublishedVersion == "1.5", "trusted title fallback failed");
        });
        await Test("first offline start has no fabricated version", async cache =>
        {
            using var client = Client(_ => throw new HttpRequestException("offline"));
            var service = new GitHubUpdateService(client, cache);
            Require(service.PublishedVersion is null, "fresh service invented a version");
            await CheckFails(service);
            Require(service.PublishedVersion is null, "offline fallback invented a version");
        });
        await Test("malformed release numbers are rejected", async cache =>
        {
            foreach (var title in new[] { "Orbit Launcher1.5 and 1.6", "Orbit Launcher 1.5-beta", "Other product 1.5" })
            {
                using var client = Client(_ => Content(Release("Release2026", title)));
                var service = new GitHubUpdateService(client, cache);
                await CheckFails(service);
                Require(service.PublishedVersion is null, "ambiguous release number accepted");
            }
        });
        await Test("corrupt or oversized disk cache is ignored", cache =>
        {
            Directory.CreateDirectory(cache);
            using var client = Client(_ => throw new HttpRequestException("offline"));
            foreach (var text in new[] { "internal-1.10.0", "1.5 and 1.6", new string('1', 1000) })
            {
                File.WriteAllText(Path.Combine(cache, "published-version.txt"), text);
                Require(new GitHubUpdateService(client, cache).PublishedVersion is null, "invalid cache trusted");
            }
            return Task.CompletedTask;
        });
        await Test("fallback cannot leave the repository or trust unrelated digits", async cache =>
        {
            foreach (var destination in new[] { "https://example.com/releases/tag/1.5", "https://github.com/other/repo/releases/tag/1.5" })
            {
                var requests = 0;
                using var client = Client(request =>
                {
                    requests++;
                    if (request.RequestUri!.Host == "api.github.com") return new(HttpStatusCode.TooManyRequests);
                    var response = new HttpResponseMessage(HttpStatusCode.Found);
                    response.Headers.Location = new Uri(destination);
                    return response;
                });
                var service = new GitHubUpdateService(client, cache);
                await CheckFails(service);
                Require(service.PublishedVersion is null && requests == 2, "untrusted redirect contacted or cached");
            }
        });
        await Test("oversized or unrelated release HTML leaves version unknown", async cache =>
        {
            foreach (var body in new[] { new string('x', 2 * 1024 * 1024 + 1), "<title>Other product 1.5</title>" })
            {
                using var client = Client(request => request.RequestUri!.Host == "api.github.com"
                    ? new(HttpStatusCode.Forbidden)
                    : request.RequestUri.AbsolutePath.EndsWith("/latest") ? PageRedirect("BigBugFix") : Content(body));
                var service = new GitHubUpdateService(client, cache);
                await CheckFails(service);
                Require(service.PublishedVersion is null, "invalid page cached a release");
            }
        });
        await Test("cancellation does not start a page fallback", async cache =>
        {
            using var cancellation = new CancellationTokenSource();
            var requests = 0;
            using var client = Client(_ => { requests++; cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
            try { await new GitHubUpdateService(client, cache).CheckAsync(new Version(9, 0), cancellation.Token); }
            catch (OperationCanceledException) { Require(requests == 1, "cancelled check started fallback"); return; }
            throw new Exception("cancellation swallowed");
        });
        return count;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(route(request));
    }
}
