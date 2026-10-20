using System.Net;
using System.Net.Sockets;

namespace OrbitLauncher.Services.Connect;

internal sealed class PendingGameProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<int> _target = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _targetPort;
    private readonly SemaphoreSlim _slots = new(8, 8);
    private readonly Task _accept;
    public int Port { get; }
    public PendingGameProxy() { _listener.Start(8); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _accept = AcceptAsync(); }
    public void SetTarget(int port) { Volatile.Write(ref _targetPort, port); _target.TrySetResult(port); }
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (!_slots.Wait(0)) { client.Dispose(); continue; }
                _ = ForwardAsync(client);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task ForwardAsync(TcpClient game)
    {
        using (game)
        using (var remote = new TcpClient { NoDelay = true })
        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                game.NoDelay = true;
                await _target.Task.WaitAsync(TimeSpan.FromSeconds(65), _stop.Token);
                int port = Volatile.Read(ref _targetPort);
                await remote.ConnectAsync(IPAddress.Loopback, port, _stop.Token);
                var gameStream = game.GetStream(); var remoteStream = remote.GetStream();
                async Task Copy(Stream from, Stream to, TcpClient destination)
                {
                    try { await from.CopyToAsync(to, 16384, connection.Token); destination.Client.Shutdown(SocketShutdown.Send); }
                    catch { connection.Cancel(); game.Close(); remote.Close(); throw; }
                }
                await Task.WhenAll(Copy(gameStream, remoteStream, remote), Copy(remoteStream, gameStream, game));
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or TimeoutException or ObjectDisposedException) { }
            finally { _slots.Release(); }
        }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); _target.TrySetCanceled(); await _accept; }
}
