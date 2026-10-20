using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Utilities;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

/// <summary>Embedded texture-only Java agent. Never configures another authentication server.</summary>
public static class LicensedSkinBridge
{
    public const string Version = "1.0.1";
    private const string ResourceName = "OrbitLauncher.SkinBridge.Jar";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Lazy<byte[]> Payload = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("В сборке отсутствует компонент скинов Ely.by.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    });
    public static async Task<string> EnsureAsync(CancellationToken token = default, string? directory = null)
    {
        await Gate.WaitAsync(token);
        try
        {
            directory ??= Path.Combine(AppPaths.Minecraft, "orbit", "skin-bridge");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "orbit-skin-bridge-" + Version + ".jar");
            var hash = SHA256.HashData(Payload.Value);
            if (File.Exists(path))
            {
                await using var input = File.OpenRead(path);
                if (CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input, token), hash)) return path;
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await File.WriteAllBytesAsync(temporary, Payload.Value, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
        finally { Gate.Release(); }
    }
    public static MArgument[] Arguments(string path, string viewerUuid, string viewerName)
    {
        if (!Guid.TryParse(viewerUuid, out var viewer)) throw new InvalidDataException("Microsoft не вернул корректный UUID игрока.");
        if (!Regex.IsMatch(viewerName ?? "", "^[A-Za-z0-9_]{3,16}$")) throw new InvalidDataException("Microsoft не вернул корректный ник игрока.");
        return new[] {
            new MArgument("-javaagent:" + Path.GetFullPath(path)),
            new MArgument("-Dorbit.skinbridge.viewer=" + viewer.ToString("N")),
            new MArgument("-Dorbit.skinbridge.viewerName=" + viewerName)
        };
    }

    public static bool HasCustomSkinLoader(GameInstance instance)
    {
        if (instance.Loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(instance.ModsPath)) return false;
        foreach (var path in Directory.EnumerateFiles(instance.ModsPath, "*.jar"))
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                if (zip.GetEntry("customskinloader/CustomSkinLoader.class") is not null || zip.Entries.Any(entry =>
                    entry.FullName.StartsWith("customskinloader/bootstrap/", StringComparison.Ordinal) && entry.FullName.EndsWith(".class", StringComparison.Ordinal))) return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return false;
    }
}
