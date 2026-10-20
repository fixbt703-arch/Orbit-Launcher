using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using System.Security.Cryptography;

namespace OrbitLauncher.Services;

/// <summary>Limit simultaneous connections and retry whole files, never commit an incomplete component.</summary>
public sealed class ReliableGameInstaller : ParallelGameInstaller
{
    private readonly HttpClient _client;
    public ReliableGameInstaller(HttpClient client) : base(4, 4, 64, client) => _client = client;
    protected override async Task Download(GameFile file, IProgress<ByteProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Url)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        for (var attempt = 0; ; attempt++)
        {
            var temporary = file.Path + ".orbit-download-" + Guid.NewGuid().ToString("N");
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(file.Size / 65536d, 90, 900)));
                using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
                request.Options.Set(LauncherNetwork.CompatibilityTransportKey, attempt > 0);
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long received = 0;
                await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[65536];
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false); if (read == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false); received += read;
                        progress?.Report(new ByteProgress(file.Size, received));
                    }
                }
                if (file.Size > 0 && received != file.Size) throw new IOException("Загрузка компонента Minecraft не завершена.");
                if (!string.IsNullOrWhiteSpace(file.Hash))
                {
                    await using var stream = File.OpenRead(temporary);
                    if (!Convert.ToHexString(await SHA1.HashDataAsync(stream, token).ConfigureAwait(false)).Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Не совпала контрольная сумма компонента Minecraft.");
                }
                token.ThrowIfCancellationRequested(); File.Move(temporary, file.Path, true); return;
            }
            catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested && ex is HttpRequestException or IOException or OperationCanceledException)
            { await Task.Delay(300 * (attempt + 1), token).ConfigureAwait(false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
