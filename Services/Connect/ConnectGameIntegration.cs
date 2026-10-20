using System.Security.Cryptography;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services.Connect;

public static class ConnectGameIntegration
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<string> EnsureAgentAsync(CancellationToken token = default, string? directory = null)
    {
        await Gate.WaitAsync(token);
        try
        {
            using var resource = typeof(ConnectGameIntegration).Assembly.GetManifestResourceStream("OrbitLauncher.Connect.Agent")
                ?? throw new IOException("В лаунчере отсутствует адаптер Orbit Connect.");
            using var output = new MemoryStream(); await resource.CopyToAsync(output, token); byte[] bytes = output.ToArray();
            string digest = Convert.ToHexString(SHA256.HashData(bytes));
            directory ??= Path.Combine(AppPaths.Root, "connect", "agent"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "orbit-connect-" + digest[..16].ToLowerInvariant() + ".jar");
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                if (Convert.ToHexString(await SHA256.HashDataAsync(file, token)) == digest) return path;
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try { await File.WriteAllBytesAsync(temporary, bytes, token); token.ThrowIfCancellationRequested(); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
        finally { Gate.Release(); }
    }
    public static async Task<MArgument[]> ArgumentsAsync(GameInstance instance, CancellationToken token)
    {
        await new ConnectRuntimeInstaller().EnsureAsync(token);
        string agent = await EnsureAgentAsync(token);
        var worker = await ConnectWorkerClient.ForAsync(instance, token);
        return [new MArgument("-javaagent:" + agent), new MArgument("-Dorbit.connect.api=" + worker.Address),
            new MArgument("-Dorbit.connect.token=" + worker.Token), new MArgument("-Dorbit.connect.language=" + LocalizationService.CurrentLanguage)];
    }
    public static async Task AttachAsync(GameInstance instance, int pid)
    {
        var worker = await ConnectWorkerClient.ForAsync(instance);
        await worker.RequestAsync("attach", new { pid });
    }
}
