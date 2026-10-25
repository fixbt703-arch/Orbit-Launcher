using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;
using OrbitLauncher.Services.Connect;

namespace OrbitLauncher.Services;

public sealed class MinecraftService
{
	private readonly HttpClient _httpClient;

	private readonly List<ProcessWrapper> _runningProcesses = new List<ProcessWrapper>();

	public MinecraftService(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}

	public async Task<LaunchResult> InstallAndLaunchAsync(GameInstance instance, MSession session, LauncherSettings settings, IProgress<LaunchProgress> progress, Action<string>? log, CancellationToken cancellationToken = default(CancellationToken), bool useEly = false, bool showElySkins = false)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(instance.DirectoryPath);
		var launchLog = new GameLaunchLog(instance.DirectoryPath, session.AccessToken, session.ClientToken);
		var originalLog = log;
		log = message => { launchLog.Write(message); originalLog?.Invoke(message); };
		try
		{
    		await new OrbitClientService(_httpClient).EnsureInstalledAsync(instance, progress, cancellationToken);
    		Directory.CreateDirectory(AppPaths.Minecraft);
    		MinecraftPath minecraftPath = new MinecraftPath(AppPaths.Minecraft);
    		MinecraftPath instancePath = CreateInstanceMinecraftPath(instance, minecraftPath);
    		MinecraftLauncher launcher = CreateLauncher(minecraftPath);
    		IProgress<InstallerProgressChangedEventArgs> fileProgress = new Progress<InstallerProgressChangedEventArgs>(delegate(InstallerProgressChangedEventArgs e)
    		{
    			double percent = ((e.TotalTasks <= 0) ? 0.0 : ((double)e.ProgressedTasks * 100.0 / (double)e.TotalTasks));
    			progress.Report(new LaunchProgress(percent, string.IsNullOrWhiteSpace(e.Name) ? "Подготовка файлов…" : e.Name));
    		});
    		IProgress<ByteProgress> byteProgress = new Progress<ByteProgress>(delegate(ByteProgress e)
    		{
    			if (e.TotalBytes > 0)
    			{
    				progress.Report(new LaunchProgress(e.ToRatio() * 100.0, "Загрузка компонентов Minecraft…"));
    			}
    		});
    		string versionId;
    		if (!string.IsNullOrWhiteSpace(instance.LaunchVersionId))
    		{
    			versionId = instance.LaunchVersionId;
    		}
    		else if (string.Equals(instance.Loader, "Fabric", StringComparison.OrdinalIgnoreCase))
    		{
    			progress.Report(new LaunchProgress(2.0, "Установка Fabric Loader…"));
    			FabricInstaller fabricInstaller = new FabricInstaller(_httpClient);
    			string text = ((!string.IsNullOrWhiteSpace(instance.LoaderVersion)) ? (await fabricInstaller.Install(instance.GameVersion, instance.LoaderVersion, minecraftPath)) : (await fabricInstaller.Install(instance.GameVersion, minecraftPath)));
    			versionId = text;
    		}
    		else if (string.Equals(instance.Loader, "Forge", StringComparison.OrdinalIgnoreCase))
    		{
    			progress.Report(new LaunchProgress(2.0, "Установка Forge…"));
    			ForgeInstaller forgeInstaller = new ForgeInstaller(launcher);
    			ForgeInstallOptions options = new ForgeInstallOptions
    			{
    				FileProgress = fileProgress,
    				ByteProgress = byteProgress,
    				InstallerOutput = new Progress<string>(delegate(string message)
    				{
    					log?.Invoke(message);
    				})
    			};
    			string text = ((!string.IsNullOrWhiteSpace(instance.LoaderVersion)) ? (await forgeInstaller.Install(instance.GameVersion, instance.LoaderVersion, options)) : (await forgeInstaller.Install(instance.GameVersion, options)));
    			versionId = text;
    		}
    		else if (string.Equals(instance.Loader, "Quilt", StringComparison.OrdinalIgnoreCase))
    		{
    			progress.Report(new LaunchProgress(2.0, "Установка Quilt Loader…"));
    			versionId = await InstallQuiltAsync(instance, settings, minecraftPath, cancellationToken);
    		}
    		else if (string.Equals(instance.Loader, "NeoForge", StringComparison.OrdinalIgnoreCase))
    		{
    			progress.Report(new LaunchProgress(2.0, "Подготовка NeoForge…"));
    			versionId = await InstallNeoForgeAsync(instance, settings, launcher, progress, log, cancellationToken);
    		}
    		else
    		{
    			versionId = instance.GameVersion;
    		}
    		progress.Report(new LaunchProgress(5.0, "Установка Minecraft " + versionId + "…"));
    		EventHandler<InstallerProgressChangedEventArgs> fileHandler = delegate(object? _, InstallerProgressChangedEventArgs e)
    		{
    			fileProgress.Report(e);
    		};
    		EventHandler<ByteProgress> byteHandler = delegate(object? _, ByteProgress e)
    		{
    			byteProgress.Report(e);
    		};
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
    		MLaunchOption mLaunchOption = new MLaunchOption
    		{
    			Session = session,
    			MinimumRamMb = instance.MinimumRamMb,
    			MaximumRamMb = instance.MaximumRamMb,
    			GameLauncherName = "OrbitLauncher",
    			GameLauncherVersion = typeof(MinecraftService).Assembly.GetName().Version!.ToString(3),
    			Path = instancePath
    		};
    		if (!string.IsNullOrWhiteSpace(instance.JavaPath))
    		{
    			mLaunchOption.JavaPath = instance.JavaPath;
    		}
    		if (useEly)
    		{
    			progress.Report(new LaunchProgress(98.0, "Подключение скинов Ely.by…"));
    			var agent = await ElyGameIntegration.EnsureAgentAsync(_httpClient, cancellationToken);
    			mLaunchOption.ExtraJvmArguments = new[] { ElyGameIntegration.AgentArgument(agent) };
    		}
    		if ((useEly || showElySkins) && !LicensedSkinBridge.HasCustomSkinLoader(instance))
    		{
    			progress.Report(new LaunchProgress(98.0, "Подключение скинов Ely.by…"));
    			var agent = await LicensedSkinBridge.EnsureAsync(cancellationToken);
    			mLaunchOption.ExtraJvmArguments = (mLaunchOption.ExtraJvmArguments ?? Array.Empty<MArgument>()).Concat(
                    LicensedSkinBridge.Arguments(agent, session.UUID ?? "", session.Username ?? "", includeViewer: settings.OfflineModeEnabled)).ToArray();
    		}
            bool connectReady = false;
            if (instance.OrbitConnectEnabled)
            {
                try
                {
                    progress.Report(new LaunchProgress(98.0, "Подготовка Orbit Connect…"));
                    var arguments = await ConnectGameIntegration.ArgumentsAsync(instance, cancellationToken);
                    mLaunchOption.ExtraJvmArguments = (mLaunchOption.ExtraJvmArguments ?? Array.Empty<MArgument>()).Concat(arguments).ToArray();
                    connectReady = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { log?.Invoke("Orbit Connect недоступен. Игра запустится; повторите подключение через панель лаунчера."); }
            }
            var datapacks = new DatapackWorldCreationBridge(instance.DirectoryPath);
            Process? process = null;
            ProcessWrapper? wrapper = null;
            try
            {
                mLaunchOption.ExtraJvmArguments = (mLaunchOption.ExtraJvmArguments ?? Array.Empty<MArgument>())
                    .Append(new MArgument("-Djava.io.tmpdir=" + datapacks.TemporaryDirectory)).ToArray();
                progress.Report(new LaunchProgress(100.0, "Запуск игры…"));
                cancellationToken.ThrowIfCancellationRequested();
                process = await launcher.BuildProcessAsync(versionId, mLaunchOption);
                cancellationToken.ThrowIfCancellationRequested();
                wrapper = new ProcessWrapper(process);
                wrapper.OutputReceived += (_, message) => log?.Invoke(message);
                _runningProcesses.Add(wrapper);
                wrapper.StartWithEvents();
                int processId = process.Id;
                if (connectReady)
                {
                    try { await ConnectGameIntegration.AttachAsync(instance, processId); }
                    catch (Exception) { log?.Invoke("Не удалось связать Orbit Connect с игрой. Игра продолжает работать."); }
                }
                _ = ObserveExitAsync(wrapper, process, datapacks, launchLog);
                return new LaunchResult(versionId, processId);
            }
            catch
            {
                datapacks.Dispose();
                if (wrapper is not null) _runningProcesses.Remove(wrapper);
                process?.Dispose();
                throw;
            }
        }
        catch (Exception error)
        {
            launchLog.Write(error.ToString()); launchLog.Dispose(); throw;
        }
    }

    private MinecraftLauncher CreateLauncher(MinecraftPath path)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(path, _httpClient);
        parameters.GameInstaller = new ReliableGameInstaller(_httpClient);
        return new MinecraftLauncher(parameters);
    }

	private async Task<string> InstallQuiltAsync(GameInstance instance, LauncherSettings settings, MinecraftPath minecraftPath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
		{
			throw new InvalidOperationException("Для Quilt не выбрана версия загрузчика.");
		}
		string value = settings.QuiltMetaApi.TrimEnd('/');
		string requestUri = $"{value}/versions/loader/{Uri.EscapeDataString(instance.GameVersion)}/{Uri.EscapeDataString(instance.LoaderVersion)}/profile/json";
		using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken);
		response.EnsureSuccessStatusCode();
		string text = await response.Content.ReadAsStringAsync(cancellationToken);
		using JsonDocument document = JsonDocument.Parse(text);
		string? versionId = document.RootElement.GetProperty("id").GetString();
		if (string.IsNullOrWhiteSpace(versionId))
		{
			throw new InvalidDataException("Quilt API не вернул ID профиля запуска.");
		}
		string text2 = Path.Combine(minecraftPath.Versions, versionId);
		Directory.CreateDirectory(text2);
		await File.WriteAllTextAsync(Path.Combine(text2, versionId + ".json"), text, cancellationToken);
		return versionId;
	}

	private async Task<string> InstallNeoForgeAsync(GameInstance instance, LauncherSettings settings, MinecraftLauncher launcher, IProgress<LaunchProgress> progress, Action<string>? log, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
		{
			throw new InvalidOperationException("Для NeoForge не выбрана версия загрузчика.");
		}
		progress.Report(new LaunchProgress(4.0, "Установка Minecraft " + instance.GameVersion + "…"));
		await launcher.InstallAsync(instance.GameVersion, cancellationToken);
		string? javaPath = await Task.Run(() => ResolveJavaPath(instance), cancellationToken);
		if (string.IsNullOrWhiteSpace(javaPath))
		{
			throw new InvalidOperationException("NeoForge требуется Java. Укажите javaw.exe в настройках сборки и повторите запуск.");
		}
		string text = settings.NeoForgeMavenMetadataApi.Trim();
		string text2 = "/maven-metadata.xml";
		string text3;
		if (!text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
		{
			text3 = text.TrimEnd('/');
		}
		else
		{
			string text4 = text;
			int length = text2.Length;
			text3 = text4.Substring(0, text4.Length - length);
		}
		string value = text3;
		string value2 = Uri.EscapeDataString(instance.LoaderVersion);
		string requestUri = $"{value}/{value2}/neoforge-{value2}-installer.jar";
		string text5 = Path.Combine(AppPaths.Cache, "neoforge");
		Directory.CreateDirectory(text5);
		string installerPath = Path.Combine(text5, "neoforge-" + instance.LoaderVersion + "-installer.jar");
		progress.Report(new LaunchProgress(18.0, "Загрузка NeoForge " + instance.LoaderVersion + "…"));
		using (HttpResponseMessage response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
		{
			response.EnsureSuccessStatusCode();
			await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
			await using FileStream output = File.Create(installerPath);
			await input.CopyToAsync(output, cancellationToken);
		}
		progress.Report(new LaunchProgress(45.0, "Установка профиля NeoForge…"));
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = javaPath,
			Arguments = $"-jar \"{installerPath}\" --installClient \"{AppPaths.Minecraft}\"",
			WorkingDirectory = AppPaths.Minecraft,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using Process installer = new Process
		{
			StartInfo = startInfo
		};
		installer.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				log?.Invoke(e.Data);
			}
		};
		installer.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				log?.Invoke(e.Data);
			}
		};
		if (!installer.Start())
		{
			throw new InvalidOperationException("Не удалось запустить установщик NeoForge.");
		}
		installer.BeginOutputReadLine();
		installer.BeginErrorReadLine();
		try
		{
			await installer.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			if (!installer.HasExited)
			{
				installer.Kill(entireProcessTree: true);
			}
			throw;
		}
		if (installer.ExitCode != 0)
		{
			throw new InvalidOperationException($"Установщик NeoForge завершился с кодом {installer.ExitCode}.");
		}
        return FindNeoForgeProfile(instance, Path.Combine(AppPaths.Minecraft, "versions"));
    }

    internal static string FindNeoForgeProfile(GameInstance instance, string versionsRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(versionsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(directory);
            var path = Path.Combine(directory, folder + ".json");
            if (!folder.Contains("neoforge", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var metadata = json.RootElement;
                if (!metadata.TryGetProperty("id", out var id) || id.GetString() != folder) continue;
                if (metadata.TryGetProperty("inheritsFrom", out var game) && game.GetString() != instance.GameVersion) continue;
                if (metadata.TryGetProperty("libraries", out var libraries) && libraries.EnumerateArray().Any(library =>
                    library.TryGetProperty("name", out var name) && name.GetString() is { } value
                    && value.Split(':') is var parts && parts.Length >= 3 && parts[0] == "net.neoforged" && parts[1] == "neoforge" && parts[2] == instance.LoaderVersion)) return folder;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                // An unusable installed profile must not hide a later exact match.
            }
        }
        throw new InvalidDataException("Не найден точный профиль NeoForge " + instance.LoaderVersion + " для Minecraft " + instance.GameVersion + ".");
    }

    internal static string? ResolveJavaPath(GameInstance instance, string? minecraftRoot = null)
    {
        minecraftRoot ??= AppPaths.Minecraft;
        var metadataPath = Path.Combine(minecraftRoot, "versions", instance.GameVersion, instance.GameVersion + ".json");
        int required = 8; string? component = null;
        if (File.Exists(metadataPath))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (json.RootElement.TryGetProperty("javaVersion", out var java))
            {
                required = java.GetProperty("majorVersion").GetInt32();
                component = java.TryGetProperty("component", out var name) ? name.GetString() : null;
            }
        }
        static int Major(string executable)
        {
            var folder = Path.GetDirectoryName(Path.GetDirectoryName(executable));
            var release = folder is null ? "" : Path.Combine(folder, "release");
            if (!File.Exists(release)) return 0;
            var match = Regex.Match(File.ReadAllText(release), "JAVA_VERSION=\"(?:1\\.)?(\\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out var major) ? major : 0;
        }
        if (!string.IsNullOrWhiteSpace(instance.JavaPath))
        {
            var candidate = Path.Combine(Path.GetDirectoryName(instance.JavaPath) ?? "", "java.exe");
            if (!File.Exists(candidate)) candidate = instance.JavaPath;
            if (!File.Exists(candidate) || Major(candidate) < required)
                throw new InvalidOperationException($"Для Minecraft {instance.GameVersion} нужна Java {required} или новее. Выберите подходящий javaw.exe в настройках сборки.");
            return candidate;
        }
        var runtime = Path.Combine(minecraftRoot, "runtime");
        if (!Directory.Exists(runtime)) return null;
        var candidates = Directory.EnumerateFiles(runtime, "java.exe", SearchOption.AllDirectories)
            .Select(path => (Path: path, Major: Major(path))).Where(candidate => candidate.Major == required)
            .OrderByDescending(candidate => component is not null && candidate.Path.Split(Path.DirectorySeparatorChar).Contains(component, StringComparer.OrdinalIgnoreCase));
        return candidates.Select(candidate => candidate.Path).FirstOrDefault();
    }

	private async Task ObserveExitAsync(ProcessWrapper wrapper, Process process, DatapackWorldCreationBridge datapacks, GameLaunchLog log)
	{
		try
		{
			await wrapper.WaitForExitTaskAsync();
		}
		catch
		{
		}
		finally
		{
            try { if (process.HasExited) log.Write("Minecraft exit code: " + process.ExitCode); }
            catch (InvalidOperationException) { }
            log.Dispose();
            datapacks.Dispose();
			_runningProcesses.Remove(wrapper);
			process.Dispose();
		}
	}

	private static MinecraftPath CreateInstanceMinecraftPath(GameInstance instance, MinecraftPath sharedPath)
	{
		return new MinecraftPath(instance.DirectoryPath)
		{
			Library = sharedPath.Library,
			Versions = sharedPath.Versions,
			Resource = sharedPath.Resource,
			Assets = sharedPath.Assets,
			Runtime = sharedPath.Runtime
		};
	}
}
