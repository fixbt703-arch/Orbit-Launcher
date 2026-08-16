using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Models;
using System.Diagnostics;
using System.Text.Json;

namespace OrbitLauncher.Services;

public sealed class MinecraftService
{
    private readonly HttpClient _httpClient;
    private readonly List<ProcessWrapper> _runningProcesses = [];

    public MinecraftService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<LaunchResult> InstallAndLaunchAsync(
        GameInstance instance,
        CmlLib.Core.Auth.MSession session,
        LauncherSettings settings,
        IProgress<LaunchProgress> progress,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(instance.DirectoryPath);
        var minecraftPath = new MinecraftPath(instance.DirectoryPath);
        var launcher = new MinecraftLauncher(minecraftPath);

        IProgress<InstallerProgressChangedEventArgs> fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
        {
            var ratio = e.TotalTasks <= 0 ? 0 : e.ProgressedTasks * 100d / e.TotalTasks;
            progress.Report(new LaunchProgress(ratio, string.IsNullOrWhiteSpace(e.Name) ? "Подготовка файлов…" : e.Name));
        });
        IProgress<ByteProgress> byteProgress = new Progress<ByteProgress>(e =>
        {
            if (e.TotalBytes > 0)
                progress.Report(new LaunchProgress(e.ToRatio() * 100d, "Загрузка компонентов Minecraft…"));
        });

        string versionId;
        if (!string.IsNullOrWhiteSpace(instance.LaunchVersionId))
        {
            versionId = instance.LaunchVersionId;
        }
        else if (string.Equals(instance.Loader, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new LaunchProgress(2, "Установка Fabric Loader…"));
            var installer = new FabricInstaller(_httpClient);
            versionId = string.IsNullOrWhiteSpace(instance.LoaderVersion)
                ? await installer.Install(instance.GameVersion, minecraftPath)
                : await installer.Install(instance.GameVersion, instance.LoaderVersion, minecraftPath);
        }
        else if (string.Equals(instance.Loader, "Forge", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new LaunchProgress(2, "Установка Forge…"));
            var installer = new ForgeInstaller(launcher);
            var options = new ForgeInstallOptions
            {
                FileProgress = fileProgress,
                ByteProgress = byteProgress,
                InstallerOutput = new Progress<string>(message => log?.Invoke(message))
            };
            versionId = string.IsNullOrWhiteSpace(instance.LoaderVersion)
                ? await installer.Install(instance.GameVersion, options)
                : await installer.Install(instance.GameVersion, instance.LoaderVersion, options);
        }
        else if (string.Equals(instance.Loader, "Quilt", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new LaunchProgress(2, "Установка Quilt Loader…"));
            versionId = await InstallQuiltAsync(instance, settings, minecraftPath, cancellationToken);
        }
        else if (string.Equals(instance.Loader, "NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new LaunchProgress(2, "Подготовка NeoForge…"));
            versionId = await InstallNeoForgeAsync(instance, settings, launcher, progress, log, cancellationToken);
        }
        else
        {
            versionId = instance.GameVersion;
        }

        progress.Report(new LaunchProgress(5, $"Установка Minecraft {versionId}…"));
        EventHandler<InstallerProgressChangedEventArgs> fileHandler = (_, e) => fileProgress.Report(e);
        EventHandler<ByteProgress> byteHandler = (_, e) => byteProgress.Report(e);
        launcher.FileProgressChanged += fileHandler;
        launcher.ByteProgressChanged += byteHandler;
        try
        {
            await launcher.InstallAsync(versionId, cancellationToken);
        }
        finally
        {
            launcher.FileProgressChanged -= fileHandler;
            launcher.ByteProgressChanged -= byteHandler;
        }

        var launchOption = new MLaunchOption
        {
            Session = session,
            MinimumRamMb = instance.MinimumRamMb,
            MaximumRamMb = instance.MaximumRamMb,
            GameLauncherName = "OrbitLauncher",
            GameLauncherVersion = "1.2.0"
        };
        if (!string.IsNullOrWhiteSpace(instance.JavaPath))
            launchOption.JavaPath = instance.JavaPath;

        progress.Report(new LaunchProgress(100, "Запуск игры…"));
        var process = await launcher.BuildProcessAsync(versionId, launchOption);
        var wrapper = new ProcessWrapper(process);
        wrapper.OutputReceived += (_, message) => log?.Invoke(message);
        _runningProcesses.Add(wrapper);
        wrapper.StartWithEvents();
        _ = ObserveExitAsync(wrapper, process);
        return new LaunchResult(versionId, process.Id);
    }

    private async Task<string> InstallQuiltAsync(
        GameInstance instance,
        LauncherSettings settings,
        MinecraftPath minecraftPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
            throw new InvalidOperationException("Для Quilt не выбрана версия загрузчика.");

        var baseUrl = settings.QuiltMetaApi.TrimEnd('/');
        var url = $"{baseUrl}/versions/loader/{Uri.EscapeDataString(instance.GameVersion)}/{Uri.EscapeDataString(instance.LoaderVersion)}/profile/json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var versionId = document.RootElement.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(versionId))
            throw new InvalidDataException("Quilt API не вернул ID профиля запуска.");

        var versionDirectory = Path.Combine(minecraftPath.Versions, versionId);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, versionId + ".json"), json, cancellationToken);
        return versionId;
    }

    private async Task<string> InstallNeoForgeAsync(
        GameInstance instance,
        LauncherSettings settings,
        MinecraftLauncher launcher,
        IProgress<LaunchProgress> progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
            throw new InvalidOperationException("Для NeoForge не выбрана версия загрузчика.");

        progress.Report(new LaunchProgress(4, $"Установка Minecraft {instance.GameVersion}…"));
        await launcher.InstallAsync(instance.GameVersion, cancellationToken);

        var javaPath = ResolveJavaPath(instance);
        if (string.IsNullOrWhiteSpace(javaPath))
            throw new InvalidOperationException("NeoForge требуется Java. Укажите javaw.exe в настройках сборки и повторите запуск.");

        var metadataUrl = settings.NeoForgeMavenMetadataApi.Trim();
        var metadataFile = "/maven-metadata.xml";
        var artifactBase = metadataUrl.EndsWith(metadataFile, StringComparison.OrdinalIgnoreCase)
            ? metadataUrl[..^metadataFile.Length]
            : metadataUrl.TrimEnd('/');
        var loaderVersion = Uri.EscapeDataString(instance.LoaderVersion);
        var installerUrl = $"{artifactBase}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
        var cacheDirectory = Path.Combine(instance.DirectoryPath, ".orbit", "cache");
        Directory.CreateDirectory(cacheDirectory);
        var installerPath = Path.Combine(cacheDirectory, $"neoforge-{instance.LoaderVersion}-installer.jar");

        progress.Report(new LaunchProgress(18, $"Загрузка NeoForge {instance.LoaderVersion}…"));
        using (var response = await _httpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(installerPath);
            await input.CopyToAsync(output, cancellationToken);
        }

        progress.Report(new LaunchProgress(45, "Установка профиля NeoForge…"));
        var processInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-jar \"{installerPath}\" --installClient \"{instance.DirectoryPath}\"",
            WorkingDirectory = instance.DirectoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var installer = new Process { StartInfo = processInfo };
        installer.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke(e.Data); };
        installer.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke(e.Data); };
        if (!installer.Start())
            throw new InvalidOperationException("Не удалось запустить установщик NeoForge.");
        installer.BeginOutputReadLine();
        installer.BeginErrorReadLine();
        try
        {
            await installer.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!installer.HasExited)
                installer.Kill(entireProcessTree: true);
            throw;
        }
        if (installer.ExitCode != 0)
            throw new InvalidOperationException($"Установщик NeoForge завершился с кодом {installer.ExitCode}.");

        var versionsDirectory = Path.Combine(instance.DirectoryPath, "versions");
        var installedVersion = Directory.Exists(versionsDirectory)
            ? Directory.EnumerateDirectories(versionsDirectory)
                .Select(Path.GetFileName)
                .Where(name => name is not null
                               && name.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                               && name.Contains(instance.LoaderVersion, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault()
            : null;
        return installedVersion
               ?? throw new InvalidDataException("NeoForge установлен, но профиль запуска не найден.");
    }

    private static string? ResolveJavaPath(GameInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))
        {
            var directory = Path.GetDirectoryName(instance.JavaPath)!;
            var java = Path.Combine(directory, "java.exe");
            return File.Exists(java) ? java : instance.JavaPath;
        }

        var runtimeDirectory = Path.Combine(instance.DirectoryPath, "runtime");
        if (Directory.Exists(runtimeDirectory))
        {
            var java = Directory.EnumerateFiles(runtimeDirectory, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (java is not null)
                return java;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "java.exe"))
            .FirstOrDefault(File.Exists);
    }

    private async Task ObserveExitAsync(ProcessWrapper wrapper, Process process)
    {
        try
        {
            await wrapper.WaitForExitTaskAsync();
        }
        catch
        {
            // Process lifetime errors should not crash the launcher UI.
        }
        finally
        {
            _runningProcesses.Remove(wrapper);
            process.Dispose();
        }
    }
}
