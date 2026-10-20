using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services.Connect;

public sealed record ConnectWorkerConfiguration(GameInstance Instance, string InstanceDirectory, string Token);

public sealed class ConnectWorkerClient
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<ConnectWorkerClient>>> Clients = new();
    private readonly HttpClient _http;
    private readonly Process _worker;
    public string Address => _http.BaseAddress!.AbsoluteUri;
    public string Token { get; }
    private ConnectWorkerClient(Process process, string address, string token)
    {
        _worker = process; Token = token;
        _http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    public static async Task<ConnectWorkerClient> ForAsync(GameInstance instance, CancellationToken token = default)
    {
        var entry = Clients.GetOrAdd(instance.Id, _ => new Lazy<Task<ConnectWorkerClient>>(() => StartAsync(instance)));
        var task = entry.Value;
        try
        {
            var client = await task.WaitAsync(token);
            if (!client._worker.HasExited) return client;
            Clients.TryRemove(new KeyValuePair<string, Lazy<Task<ConnectWorkerClient>>>(instance.Id, entry)); return await ForAsync(instance, token);
        }
        catch { if (task.IsFaulted || task.IsCanceled) Clients.TryRemove(new KeyValuePair<string, Lazy<Task<ConnectWorkerClient>>>(instance.Id, entry)); throw; }
    }
    private static async Task<ConnectWorkerClient> StartAsync(GameInstance instance)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string folder = Path.Combine(AppPaths.Root, "connect", "control"); Directory.CreateDirectory(folder);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(folder).SetAccessControl(acl);
        string config = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new ConnectWorkerConfiguration(instance, instance.DirectoryPath, token)));
        string executable = string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "OrbitLauncher", StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath! : Path.Combine(AppContext.BaseDirectory, "OrbitLauncher.exe");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--connect-worker"); info.ArgumentList.Add(config);
        var process = Process.Start(info) ?? throw new IOException("Не удалось запустить Orbit Connect.");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(config + ".ready"))
            { deadline.Token.ThrowIfCancellationRequested(); if (process.HasExited) throw new IOException("Orbit Connect не запустился."); await Task.Delay(100, deadline.Token); }
            string address = await File.ReadAllTextAsync(config + ".ready", deadline.Token);
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1"
                || !ConnectGamePorts.IsOwned(uri.Port, process.Id)) throw new IOException("Не удалось проверить локальный Orbit Connect.");
            return new(process, address, token);
        }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process.Dispose();
            foreach (string file in new[] { config, config + ".ready", config + ".ready.tmp" })
                try { File.Delete(file); } catch (IOException) { }
            throw;
        }
    }
    public async Task<ConnectStatus> RequestAsync(string path, object? body = null, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(token);
        if (json.Length > 16384) throw new IOException("Некорректный ответ Orbit Connect.");
        return JsonSerializer.Deserialize<ConnectStatus>(json) ?? throw new IOException("Orbit Connect не вернул состояние.");
    }
    public static async Task RunWorkerAsync(string path)
    {
        string root = Path.GetFullPath(Path.Combine(AppPaths.Root, "connect", "control")) + Path.DirectorySeparatorChar;
        path = Path.GetFullPath(path);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length > 32768) return;
        try
        {
            var config = JsonSerializer.Deserialize<ConnectWorkerConfiguration>(await File.ReadAllTextAsync(path));
            if (config is null || config.Token.Length != 64) return;
            config.Instance.DirectoryPath = config.InstanceDirectory;
            await using var session = new ConnectSession(config.Instance);
            await using var api = new ConnectControlServer(session.HandleAsync, config.Token);
            await File.WriteAllTextAsync(path + ".ready.tmp", api.Address); File.Move(path + ".ready.tmp", path + ".ready", true);
            while (!session.Finished) await Task.Delay(1000);
        }
        finally
        {
            foreach (string file in new[] { path, path + ".ready", path + ".ready.tmp" })
                try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
        }
    }
}
