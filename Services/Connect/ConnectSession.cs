using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services.Connect;

public sealed record ConnectStatus(string Phase, string Message, string Invitation = "", string Address = "", double? PingMs = null, string RoomId = "");

public sealed class ConnectSession : IAsyncDisposable
{
    private readonly GameInstance _instance;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _operationStop;
    private Task _operation = Task.CompletedTask;
    private EasyTierTransport? _transport;
    private TerracottaTransport? _terracotta;
    private PendingGameProxy? _proxy;
    private ConnectInvitation? _invitation;
    private readonly Task _monitor;
    private ConnectStatus _status = new("idle", "Откройте мир для сети или вставьте приглашение друга.");
    private int _gamePid, _lanPort;
    private bool _hosting;
    private bool _stopping, _usingTerracotta;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private readonly HashSet<string> _connectors = new(StringComparer.Ordinal);
    private DateTime _lastRecovery = DateTime.MinValue;
    private DateTime _gameStarted;
    private string _requestedCode = "";
    private DateTime _lastAccess = DateTime.UtcNow;
    public bool Finished { get; private set; }
    public ConnectStatus Status { get { lock (_sync) return _status; } }
    private void Set(ConnectStatus status) { lock (_sync) _status = status with { RoomId = RoomId(_requestedCode) }; }
    private static string RoomId(string code) => code.Length == 0 ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    public ConnectSession(GameInstance instance) { _instance = instance; _monitor = MonitorAsync(); }

    public Task<string> HandleAsync(string path, string body)
    {
        _lastAccess = DateTime.UtcNow;
        try
        {
            if (path == "attach")
            {
                using var data = JsonDocument.Parse(body); int pid = data.RootElement.GetProperty("pid").GetInt32();
                using var process = Process.GetProcessById(pid);
                if (process.HasExited) throw new IOException("Игра уже завершилась.");
                _gamePid = pid;
                _gameStarted = process.StartTime.ToUniversalTime();
            }
            else if (path == "lan")
            {
                using var data = JsonDocument.Parse(body); int port = data.RootElement.GetProperty("port").GetInt32();
                if (port == 0) { _ = StopAsync(); }
                else if ((port != _lanPort || Status.Phase == "idle") && ConnectGamePorts.IsOwned(port, _gamePid))
                    Begin(token => HostAsync(port, token));
            }
            else if (path == "host")
            {
                using var data = JsonDocument.Parse(body); int port = data.RootElement.GetProperty("port").GetInt32();
                if (!ConnectGamePorts.IsOwned(port, _gamePid)) throw new IOException("Этот порт не принадлежит запущенной игре.");
                Begin(token => HostAsync(port, token));
            }
            else if (path == "join")
            {
                using var data = JsonDocument.Parse(body); string code = data.RootElement.GetProperty("code").GetString() ?? "";
                lock (_sync)
                {
                    if (_requestedCode == code && Status.Phase is "connected" or "connecting" or "starting" or "recovering") return Task.FromResult(JsonSerializer.Serialize(Status));
                    if (!_operation.IsCompleted || _stopping || Status.Phase is "hosting" or "connected" or "recovering")
                        throw new IOException("Сначала отключите текущее соединение Orbit Connect.");
                }
                if (code.StartsWith("U/", StringComparison.OrdinalIgnoreCase))
                { if (!TerracottaTransport.IsRoomCode(code)) throw new FormatException("Некорректный код Terracotta."); Begin(token => JoinTerracottaAsync(code, token), code); }
                else { var invite = ConnectInvitation.Parse(code); Begin(token => JoinAsync(invite, token), code); }
            }
            else if (path == "stop") _ = StopAsync();
            else if (path is not ("state" or "attach" or "lan")) throw new FormatException("Неизвестное действие.");
            return Task.FromResult(JsonSerializer.Serialize(Status));
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or IOException or ArgumentException or JsonException or KeyNotFoundException)
        { return Task.FromResult(JsonSerializer.Serialize(new ConnectStatus("error", e.Message))); }
    }

    private void Begin(Func<CancellationToken, Task> action, string code = "")
    {
        lock (_sync)
        {
            if (!_operation.IsCompleted || _stopping || _lifetime.IsCancellationRequested) return;
            _requestedCode = code;
            _status = new("starting", "Подготавливаем подключение…", RoomId: RoomId(code));
            _operationStop?.Dispose(); _operationStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var token = _operationStop.Token;
            _operation = Task.Run(async () =>
            {
                try
                {
                    await _stopGate.WaitAsync(token);
                    try { await CleanupTransportAsync(); } finally { _stopGate.Release(); }
                    await action(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { await CleanupTransportAsync(); }
                catch (Exception e) { await CleanupTransportAsync(); Set(new("error", Friendly(e))); }
            });
        }
    }

    private async Task HostAsync(int port, CancellationToken token)
    {
        _hosting = true; _lanPort = port;
        Set(new("starting", "Готовим подключение к миру…"));
        await ConnectMinecraftProbe.QueryAsync(port, token);
        var runtime = await new ConnectRuntimeInstaller().EnsureAsync(token);
        var nodes = await NodesAsync(token);
        _invitation = ConnectInvitation.Create(port, _instance.Name, _instance.GameVersion);
        Exception? primaryFailure = null;
        try
        {
            _transport = await EasyTierTransport.StartAsync(runtime, _invitation, true, nodes, token);
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(token); ready.CancelAfter(TimeSpan.FromSeconds(18));
            while (true)
            {
                ready.Token.ThrowIfCancellationRequested();
                try { if (await RouteHealthyAsync(ready.Token)) break; }
                catch (Exception e) when (e is IOException or OperationCanceledException or JsonException) { ready.Token.ThrowIfCancellationRequested(); }
                await AddReserveAsync(ready.Token); await Task.Delay(1000, ready.Token);
            }
        }
        catch (Exception e) when (!token.IsCancellationRequested) { primaryFailure = e; var core = Interlocked.Exchange(ref _transport, null); if (core is not null) await core.DisposeAsync(); }
        Set(new("starting", "Готовим резервное подключение…"));
        try
        {
            using var backup = CancellationTokenSource.CreateLinkedTokenSource(token); backup.CancelAfter(TimeSpan.FromSeconds(18));
            _terracotta = await TerracottaTransport.StartAsync(runtime, backup.Token);
            string code = await _terracotta.HostAsync(port, _gamePid, backup.Token);
            _invitation = _invitation with { TerracottaCode = code };
        }
        catch (Exception) when (!token.IsCancellationRequested)
        { var tc = Interlocked.Exchange(ref _terracotta, null); if (tc is not null) await tc.DisposeAsync(); }
        if (_transport is null && _terracotta is null) throw new IOException(Friendly(primaryFailure ?? new IOException("Не удалось подготовить резервное подключение.")));
        _usingTerracotta = _transport is null;
        Set(new("hosting", _usingTerracotta ? "Мир открыт через Terracotta. Скопируйте приглашение." : _terracotta is null
            ? "Мир открыт. Резерв Terracotta недоступен; резервные узлы EasyTier активны."
            : "Мир открыт. Резерв Terracotta готов.", _invitation.Encode()));
    }

    private async Task JoinAsync(ConnectInvitation invitation, CancellationToken token)
    {
        _hosting = false; _lanPort = 0; _invitation = invitation;
        _proxy = new PendingGameProxy();
        Set(new("connecting", "Ищем путь к миру друга…", Address: "127.0.0.1:" + _proxy.Port));
        var runtime = await new ConnectRuntimeInstaller().EnsureAsync(token);
        var nodes = await NodesAsync(token);
        try
        {
        _transport = await EasyTierTransport.StartAsync(runtime, invitation, false, nodes, token);
        using var ready = CancellationTokenSource.CreateLinkedTokenSource(token); ready.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            ready.Token.ThrowIfCancellationRequested();
            try
            {
                double ping = await ConnectMinecraftProbe.QueryAsync(_transport.LocalPort, ready.Token);
                _proxy.SetTarget(_transport.LocalPort);
                Set(new("connected", "Мир доступен. Подключитесь к адресу ниже в Minecraft.", Address: "127.0.0.1:" + _proxy.Port, PingMs: ping));
                return;
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or JsonException) { ready.Token.ThrowIfCancellationRequested(); }
            await AddReserveAsync(ready.Token);
            await Task.Delay(500, ready.Token);
        }
        }
        catch (Exception) when (!token.IsCancellationRequested && invitation.TerracottaCode.Length > 0)
        { await SwitchToTerracottaAsync(invitation.TerracottaCode, token); }
    }

    private async Task JoinTerracottaAsync(string code, CancellationToken token)
    {
        _hosting = false; _lanPort = 0;
        _proxy = new PendingGameProxy();
        await SwitchToTerracottaAsync(code, token);
    }

    private async Task SwitchToTerracottaAsync(string code, CancellationToken token)
    {
        var core = Interlocked.Exchange(ref _transport, null); if (core is not null) await core.DisposeAsync();
        var previous = Interlocked.Exchange(ref _terracotta, null); if (previous is not null) await previous.DisposeAsync();
        _usingTerracotta = true;
        _proxy ??= new PendingGameProxy();
        Set(new("connecting", "Подключаемся через Terracotta…", Address: "127.0.0.1:" + _proxy.Port));
        var runtime = await new ConnectRuntimeInstaller().EnsureAsync(token);
        _terracotta = await TerracottaTransport.StartAsync(runtime, token);
        string address = await _terracotta.JoinAsync(code, token);
        if (!TryLocalPort(address, out int port)) throw new IOException("Terracotta вернула недопустимый адрес.");
        double ping = await ConnectMinecraftProbe.QueryAsync(port, token);
        _proxy.SetTarget(port);
        Set(new("connected", "Мир доступен через Terracotta.", Address: "127.0.0.1:" + _proxy.Port, PingMs: ping));
    }

    private async Task MonitorAsync()
    {
        var worldPolicy = new ConnectRecoveryPolicy();
        var routePolicy = new ConnectRecoveryPolicy();
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(2000, _lifetime.Token);
                try
                {
                if (_gamePid > 0)
                {
                    bool alive;
                    try { using var p = Process.GetProcessById(_gamePid); alive = !p.HasExited && p.StartTime.ToUniversalTime() == _gameStarted; } catch (ArgumentException) { alive = false; }
                    if (!alive) { await StopAsync(); Finished = true; return; }
                }
                else if (DateTime.UtcNow - _lastAccess > TimeSpan.FromMinutes(3)) { await StopAsync(); Finished = true; return; }
                if (!_operation.IsCompleted) continue;
                if (_hosting && _lanPort > 0 && worldPolicy.Observe(ConnectGamePorts.IsOwned(_lanPort, _gamePid), false))
                { await StopAsync(); Set(new("idle", "Мир закрыт. Откройте другой мир для сети.")); continue; }
                await _stopGate.WaitAsync(_lifetime.Token);
                try
                {
                if (_stopping || !_operation.IsCompleted) continue;
                if (_transport is not null && !_usingTerracotta)
                {
                    bool healthy = false;
                    try
                    {
                        healthy = await RouteHealthyAsync(_lifetime.Token);
                    }
                    catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or InvalidOperationException) { }
                    if (healthy)
                    { routePolicy.Observe(true, false); if (Status.Phase == "recovering") Set(Status with { Phase = _hosting ? "hosting" : "connected", Message = "Маршрут восстановлен." }); }
                    else if (routePolicy.Observe(false, !_transport.IsAlive))
                    {
                        Set(Status with { Phase = "recovering", Message = "Восстанавливаем маршрут…", PingMs = null });
                        bool added = _transport.IsAlive && await AddReserveAsync(_lifetime.Token);
                        if ((!added && DateTime.UtcNow - _lastRecovery > TimeSpan.FromSeconds(60)) || !_transport.IsAlive)
                        {
                            if (_hosting && _terracotta is { IsAlive: true })
                            { var core = Interlocked.Exchange(ref _transport, null); if (core is not null) await core.DisposeAsync(); _usingTerracotta = true; Set(Status with { Phase = "hosting", Message = "Основной маршрут недоступен. Резерв Terracotta готов." }); }
                            else if (!_hosting && _invitation?.TerracottaCode is { Length: > 0 } backup)
                                BeginRecovery(backup);
                            else if (!_transport.IsAlive) { await CleanupTransportAsync(); Set(new("error", "Сетевой компонент остановился. Повторите подключение.")); }
                        }
                    }
                }
                if (_usingTerracotta && _terracotta is { IsAlive: false })
                { await CleanupTransportAsync(); Set(new("error", "Резервное соединение остановилось. Повторите подключение.")); }
                if (!_hosting && Status.Phase == "connected" && TryLocalPort(Status.Address, out int port))
                {
                    using var udp = new UdpClient();
                    byte[] packet = Encoding.UTF8.GetBytes("[MOTD]Orbit Connect[/MOTD][AD]" + port + "[/AD]");
                    try { await udp.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, 4445), _lifetime.Token); }
                    catch (SocketException) { }
                }
                }
                finally { _stopGate.Release(); }
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
                catch (Exception) { Set(Status with { Message = "Не удалось обновить состояние подключения. Повторяем проверку…" }); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Set(new("error", Friendly(e))); }
    }

    private void BeginRecovery(string backup)
    {
        lock (_sync)
        {
            if (!_operation.IsCompleted || _stopping) return;
            _operationStop?.Dispose(); _operationStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var token = _operationStop.Token;
            _operation = Task.Run(async () =>
            {
                try { await SwitchToTerracottaAsync(backup, token); Set(Status with { Message = "Резерв готов. Если Minecraft отключился, войдите в мир повторно." }); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception e) { await CleanupTransportAsync(); Set(new("error", Friendly(e))); }
            });
        }
    }
    private async Task<IReadOnlyList<ConnectNode>> NodesAsync(CancellationToken token)
    {
        var nodes = await ConnectNodeCatalog.OrderAsync(token); _connectors.Clear();
        foreach (var node in nodes) _connectors.Add(node.Endpoint);
        _lastRecovery = DateTime.UtcNow; _usingTerracotta = false; return nodes;
    }
    private async Task<bool> AddReserveAsync(CancellationToken token)
    {
        if (_transport is null || DateTime.UtcNow - _lastRecovery < TimeSpan.FromSeconds(6)) return false;
        var node = ConnectNodeCatalog.Load().FirstOrDefault(n => !_connectors.Contains(n.Endpoint));
        if (node is null) return false;
        try { await _transport.AddConnectorAsync(node, token); _connectors.Add(node.Endpoint); _lastRecovery = DateTime.UtcNow; return true; }
        catch (Exception e) when (e is IOException or OperationCanceledException) { token.ThrowIfCancellationRequested(); return false; }
    }
    private async Task<bool> RouteHealthyAsync(CancellationToken token)
    {
        if (_transport is not { IsAlive: true }) return false;
        var transport = _transport;
        if (transport is null) return false;
        using var peers = JsonDocument.Parse(await transport.PeerInfoAsync(token));
        foreach (var peer in peers.RootElement.EnumerateArray())
        {
            if (peer.GetProperty("cost").GetString() == "Local") continue;
            if (!_hosting && (!peer.TryGetProperty("ipv4", out var ip) || ip.GetString() != "10.144.144.1")) continue;
            if (!peer.TryGetProperty("lat_ms", out var ms) || !double.TryParse(ms.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ping)) continue;
            if (!_hosting) Set(Status with { PingMs = ping });
            return true;
        }
        return false;
    }

    private static bool TryLocalPort(string address, out int port)
    { port = 25565; return address == "127.0.0.1" || (address.StartsWith("127.0.0.1:") && int.TryParse(address[10..], out port) && port is > 0 and < 65536); }
    private static string Friendly(Exception e) => e is OperationCanceledException ? "Подключение заняло слишком много времени. Попробуйте ещё раз."
        : e is SocketException or HttpRequestException ? "Не удалось найти доступный маршрут. Проверьте подключение и повторите попытку."
        : e.Message.Length > 300 ? "Сетевой компонент не смог подготовить соединение. Повторите попытку." : e.Message;
    private async Task CleanupTransportAsync()
    {
        var proxy = Interlocked.Exchange(ref _proxy, null); if (proxy is not null) await proxy.DisposeAsync();
        var transport = Interlocked.Exchange(ref _transport, null); if (transport is not null) await transport.DisposeAsync();
        var tc = Interlocked.Exchange(ref _terracotta, null); if (tc is not null) await tc.DisposeAsync();
    }
    public async Task StopAsync()
    {
        await _stopGate.WaitAsync();
        try
        {
            Task operation;
            lock (_sync) { _stopping = true; _operationStop?.Cancel(); operation = _operation; }
            try { await operation; } catch (Exception) { }
            await CleanupTransportAsync(); _lanPort = 0; _hosting = false; _usingTerracotta = false; _invitation = null; _requestedCode = "";
            Set(new("idle", "Подключение закрыто."));
        }
        finally { lock (_sync) _stopping = false; _stopGate.Release(); }
    }
    public async ValueTask DisposeAsync() { _lifetime.Cancel(); await StopAsync(); await _monitor; _lifetime.Dispose(); _operationStop?.Dispose(); }
}
