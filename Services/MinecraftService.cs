using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

namespace OrbitLauncher.Services;

public sealed class MinecraftService
{
	private readonly HttpClient _httpClient;

	private readonly List<ProcessWrapper> _runningProcesses = new List<ProcessWrapper>();

	public MinecraftService(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}

	public async Task<LaunchResult> InstallAndLaunchAsync(GameInstance instance, MSession session, LauncherSettings settings, IProgress<LaunchProgress> progress, Action<string>? log, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(instance.DirectoryPath);
		await new OrbitClientService(_httpClient).EnsureInstalledAsync(instance, progress, cancellationToken);
		Directory.CreateDirectory(AppPaths.Minecraft);
		MinecraftPath minecraftPath = new MinecraftPath(AppPaths.Minecraft);
		MinecraftPath instancePath = CreateInstanceMinecraftPath(instance, minecraftPath);
		MinecraftLauncher launcher = new MinecraftLauncher(minecraftPath);
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
		progress.Report(new LaunchProgress(100.0, "Запуск игры…"));
		cancellationToken.ThrowIfCancellationRequested();
		Process process = await launcher.BuildProcessAsync(versionId, mLaunchOption);
		if (cancellationToken.IsCancellationRequested) { process.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
		ProcessWrapper processWrapper = new ProcessWrapper(process);
		processWrapper.OutputReceived += delegate(object? _, string message)
		{
			log?.Invoke(message);
		};
		_runningProcesses.Add(processWrapper);
		DatapackWorldCreationBridge datapackWorldCreationBridge = new DatapackWorldCreationBridge(instance.DirectoryPath);
		try
		{
			processWrapper.StartWithEvents();
		}
		catch
		{
			datapackWorldCreationBridge.Dispose();
			_runningProcesses.Remove(processWrapper);
			throw;
		}
		int processId = process.Id;
		_ = ObserveExitAsync(processWrapper, process, datapackWorldCreationBridge);
		return new LaunchResult(versionId, processId);
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
		string versionId = document.RootElement.GetProperty("id").GetString();
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
		string javaPath = ResolveJavaPath(instance);
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
		string path = Path.Combine(AppPaths.Minecraft, "versions");
		return (Directory.Exists(path) ? (from name in Directory.EnumerateDirectories(path).Select(Path.GetFileName)
			where name != null && name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) && name.Contains(instance.LoaderVersion, StringComparison.OrdinalIgnoreCase)
			select name).FirstOrDefault() : null) ?? throw new InvalidDataException("NeoForge установлен, но профиль запуска не найден.");
	}

	private static string? ResolveJavaPath(GameInstance instance)
	{
		if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))
		{
			string text = Path.Combine(Path.GetDirectoryName(instance.JavaPath), "java.exe");
			if (!File.Exists(text))
			{
				return instance.JavaPath;
			}
			return text;
		}
		string path = Path.Combine(AppPaths.Minecraft, "runtime");
		if (Directory.Exists(path))
		{
			string text2 = Directory.EnumerateFiles(path, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
			if (text2 != null)
			{
				return text2;
			}
		}
		return (from directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			select Path.Combine(directory.Trim(), "java.exe")).FirstOrDefault(File.Exists);
	}

	private async Task ObserveExitAsync(ProcessWrapper wrapper, Process process, DatapackWorldCreationBridge datapacks)
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
