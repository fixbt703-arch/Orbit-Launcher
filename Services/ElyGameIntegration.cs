using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public static class ElyGameIntegration
{
    public const string AgentVersion = "1.2.8";
    public const string AgentSha256 = "9c7f4343e6c82034958ffb48c14a2cb0c85928be7283103ce17da00c6d5a7b10";
    public const string ApiRoot = "https://account.ely.by/api/authlib-injector/";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<string> EnsureAgentAsync(HttpClient http, CancellationToken token, string? directory = null)
    {
        await Gate.WaitAsync(token);
        try
        {
            directory ??= Path.Combine(AppPaths.Minecraft, "orbit", "ely");
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, "authlib-injector-" + AgentVersion + ".jar");
            if (File.Exists(destination) && await IsValidAsync(destination, token)) return destination;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            using var response = await http.GetAsync("https://github.com/yushijinhun/authlib-injector/releases/download/v" + AgentVersion + "/authlib-injector-" + AgentVersion + ".jar", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token))
                await using (var output = File.Create(temporary))
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
                    {
                        if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("Некорректный размер компонента Ely.by.");
                        await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                    }
                }
                if (!await IsValidAsync(temporary, token)) throw new InvalidDataException("Не совпала контрольная сумма компонента Ely.by. Повторите загрузку.");
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return destination;
        }
        finally { Gate.Release(); }
    }

    public static MArgument AgentArgument(string path) => new("-javaagent:" + Path.GetFullPath(path) + "=" + ApiRoot);
    private static async Task<bool> IsValidAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(AgentSha256, StringComparison.OrdinalIgnoreCase);
    }
}
