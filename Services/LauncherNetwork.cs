using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace OrbitLauncher.Services;

/// <summary>Bounded HTTP/1.1 connections with a TLS 1.2 retry for incompatible packet filters.</summary>
public static class LauncherNetwork
{
    public static readonly HttpRequestOptionsKey<bool> CompatibilityTransportKey = new("OrbitCompatibilityTransport");

    public static async Task<byte[]> GetMetadataAsync(HttpClient client, string url, CancellationToken token = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(18));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Options.Set(CompatibilityTransportKey, attempt > 0);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                using var bytes = new MemoryStream(); var buffer = new byte[8192];
                while (true)
                {
                    int read = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                    if (read == 0) return bytes.ToArray();
                    if (bytes.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("Слишком большой список версий.");
                    bytes.Write(buffer, 0, read);
                }
            }
            catch (Exception ex) when (attempt == 0 && !token.IsCancellationRequested
                && (ex is IOException or OperationCanceledException || ex is HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError }))
            { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }
    public static HttpClient CreateClient(bool redirects = true)
    {
        SocketsHttpHandler Transport(bool compatibility) => new()
        {
            AllowAutoRedirect = redirects,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            MaxConnectionsPerServer = 6,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            SslOptions = new SslClientAuthenticationOptions
            { EnabledSslProtocols = compatibility ? SslProtocols.Tls12 : SslProtocols.None }
        };
        return new HttpClient(new NetworkRecoveryHandler(Transport(false), Transport(true)))
        { Timeout = TimeSpan.FromMinutes(15), DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
    }
}

public sealed class NetworkRecoveryHandler : HttpMessageHandler
{
    private readonly HttpMessageInvoker _primary, _compatibility;
    private readonly TimeSpan _headersTimeout, _idleTimeout;
    public NetworkRecoveryHandler(HttpMessageHandler primary, HttpMessageHandler compatibility,
        TimeSpan? headersTimeout = null, TimeSpan? idleTimeout = null)
    {
        _primary = new(primary, true); _compatibility = new(compatibility, true);
        _headersTimeout = headersTimeout ?? TimeSpan.FromSeconds(12);
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(20);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var retryable = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_headersTimeout);
            // Never replay login, upload, or other requests which change remote state.
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri)
            { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (!retryable) copy.Content = request.Content;
            HttpResponseMessage? response = null;
            try
            {
                bool useCompatibility = request.Options.TryGetValue(LauncherNetwork.CompatibilityTransportKey, out var preferred) && preferred;
                response = await (attempt == 0 && !useCompatibility ? _primary : _compatibility).SendAsync(copy, timeout.Token).ConfigureAwait(false);
                if (retryable && attempt == 0 && (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
                { response.Dispose(); await Task.Delay(250, token).ConfigureAwait(false); continue; }
                response.RequestMessage = request;
                response.Content = new StallProtectedContent(response.Content, _idleTimeout);
                return response;
            }
            catch (Exception ex) when (retryable && attempt == 0 && !token.IsCancellationRequested
                && ex is HttpRequestException or OperationCanceledException)
            { response?.Dispose(); await Task.Delay(150, token).ConfigureAwait(false); }
            finally { if (!retryable) copy.Content = null; }
        }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { _primary.Dispose(); _compatibility.Dispose(); } base.Dispose(disposing); }

    private sealed class StallProtectedContent : HttpContent
    {
        private readonly HttpContent _inner; private readonly TimeSpan _timeout;
        public StallProtectedContent(HttpContent inner, TimeSpan timeout)
        { _inner = inner; _timeout = timeout; foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value); }
        protected override bool TryComputeLength(out long length)
        { length = _inner.Headers.ContentLength ?? -1; return length >= 0; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        { await using var input = await CreateContentReadStreamAsync().ConfigureAwait(false); await input.CopyToAsync(stream).ConfigureAwait(false); }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        { await using var input = await CreateContentReadStreamAsync(token).ConfigureAwait(false); await input.CopyToAsync(stream, token).ConfigureAwait(false); }
        protected override async Task<Stream> CreateContentReadStreamAsync() =>
            new IdleTimeoutStream(await _inner.ReadAsStreamAsync().ConfigureAwait(false), _timeout);
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken token) =>
            new IdleTimeoutStream(await _inner.ReadAsStreamAsync(token).ConfigureAwait(false), _timeout);
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class IdleTimeoutStream(Stream inner, TimeSpan timeout) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
            try { return await inner.ReadAsync(buffer, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            { throw new IOException("Сервер перестал передавать данные. Повторите загрузку; при активном Zapret попробуйте другую стратегию.", ex); }
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
