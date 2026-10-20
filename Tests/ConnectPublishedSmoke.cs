using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services.Connect;
using OrbitLauncher.Utilities;

internal static class ConnectPublishedSmoke
{
    public static async Task RunAsync(string executable)
    {
        executable = Path.GetFullPath(executable);
        var version = FileVersionInfo.GetVersionInfo(executable);
        if (version.FileVersion is null || !version.FileVersion.StartsWith("1.5.4")) throw new Exception("Published EXE version wrong");
        string control = Path.Combine(AppPaths.Root, "connect", "control"); Directory.CreateDirectory(control);
        string config = Path.Combine(control, Guid.NewGuid().ToString("N") + ".json");
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new ConnectWorkerConfiguration(new GameInstance { Name = "Published worker test" }, "", token)));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--connect-worker"); start.ArgumentList.Add(config);
        using var worker = Process.Start(start)!; Process? game = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (!File.Exists(config + ".ready")) { if (worker.HasExited) throw new Exception("Published worker exited before readiness"); await Task.Delay(100, deadline.Token); }
            string address = await File.ReadAllTextAsync(config + ".ready", deadline.Token);
            var uri = new Uri(address);
            if (uri.Host != "127.0.0.1" || !ConnectGamePorts.IsOwned(uri.Port, worker.Id) || worker.MainWindowHandle != IntPtr.Zero) throw new Exception("Published worker identity/window mismatch");
            using var http = new HttpClient { BaseAddress = uri }; http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var state = JsonSerializer.Deserialize<ConnectStatus>(await http.GetStringAsync("state", deadline.Token));
            if (state?.Phase != "idle") throw new Exception("Published worker status invalid");
            var child = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "OrbitLauncher.Tests.exe")) { UseShellExecute = false, CreateNoWindow = true };
            child.ArgumentList.Add("--connect-pid-fixture"); game = Process.Start(child)!;
            using var body = new StringContent("{\"pid\":" + game.Id + "}", Encoding.UTF8, "application/json");
            using var attached = await http.PostAsync("attach", body, deadline.Token); attached.EnsureSuccessStatusCode();
            await game.WaitForExitAsync(deadline.Token); await worker.WaitForExitAsync(deadline.Token);
            if (File.Exists(config) || File.Exists(config + ".ready")) throw new Exception("Published worker did not clean private control files");
            Console.WriteLine("PASS actual dist/OrbitLauncher.exe 1.5.4: single-file headless worker, authenticated owned API, exits with attached game PID, private files cleaned");
        }
        finally
        {
            if (game is not null) { if (!game.HasExited) game.Kill(true); game.Dispose(); }
            if (!worker.HasExited) { worker.Kill(true); await worker.WaitForExitAsync(); }
            foreach (string path in new[] { config, config + ".ready", config + ".ready.tmp" }) File.Delete(path);
        }
    }
}
