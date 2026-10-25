using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class InstanceRepository
{
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
	private readonly string _instancesRoot;
	private readonly string _minecraftRoot;
	private readonly SemaphoreSlim _gate;
	public List<string> LoadWarnings { get; } = new();
	public InstanceRepository(string? instancesRoot = null, string? minecraftRoot = null)
	{
		_instancesRoot = Path.GetFullPath(instancesRoot ?? AppPaths.Instances).TrimEnd(Path.DirectorySeparatorChar);
		_minecraftRoot = Path.GetFullPath(minecraftRoot ?? AppPaths.Minecraft);
		_gate = Gates.GetOrAdd(_instancesRoot, _ => new SemaphoreSlim(1, 1));
	}
	private async Task<T> LockedAsync<T>(Func<Task<T>> action)
	{
		await _gate.WaitAsync();
		try { return await action(); }
		finally { _gate.Release(); }
	}
	private const string DefinitionFile = "instance.json";

	private static readonly string[] SharedMinecraftDirectories = new string[5] { "assets", "libraries", "runtime", "versions", "resources" };

	public Task<IReadOnlyList<GameInstance>> GetAllAsync() => LockedAsync(GetAllCoreAsync);
	private async Task<IReadOnlyList<GameInstance>> GetAllCoreAsync()
	{
		Directory.CreateDirectory(_instancesRoot);
		LoadWarnings.Clear();
		List<GameInstance> instances = new List<GameInstance>();
		foreach (string directory in Directory.EnumerateDirectories(_instancesRoot).ToList())
		{
			if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(directory), @"^\.orbit-new-[0-9a-f]{32}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
				|| (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
			string path = Path.Combine(directory, "instance.json");
			if (!File.Exists(path) && !File.Exists(path + ".bak"))
			{
				continue;
			}
			try
			{
				GameInstance? instance;
				instance = AtomicJsonFile.Read<GameInstance>(path);
				if (instance != null)
				{
					if (string.IsNullOrWhiteSpace(instance.Name)) instance.Name = Path.GetFileName(directory);
					if (string.IsNullOrWhiteSpace(instance.GameVersion)) throw new InvalidDataException("В описании сборки отсутствует версия Minecraft.");
					instance.DirectoryPath = directory;
					await Task.Run(delegate
					{
						EnsureFriendlyDirectory(instance, _instancesRoot);
					});
					instances.Add(instance);
					await Task.Run(delegate
					{
						MigrateSharedMinecraftFiles(instance, _minecraftRoot);
					});
					Directory.CreateDirectory(instance.ModsPath);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
			{
				LoadWarnings.Add(Path.GetFileName(directory) + ": " + ex.Message);
			}
		}
		return (from x in instances
			orderby x.LastPlayedAt ?? x.CreatedAt descending, x.Name
			select x).ToList();
	}

	public Task<GameInstance> CreateAsync(GameInstance instance) => LockedAsync(() => CommitNewAsync(instance));
	private async Task<GameInstance> CommitNewAsync(GameInstance instance, string? source = null)
	{
		Directory.CreateDirectory(_instancesRoot);
		if (string.IsNullOrWhiteSpace(instance.Name) || string.IsNullOrWhiteSpace(instance.GameVersion)) throw new InvalidDataException("Название и версия сборки не могут быть пустыми.");
		instance.Id = Guid.NewGuid().ToString("N");
		var stage = Path.GetFullPath(Path.Combine(_instancesRoot, ".orbit-new-" + Guid.NewGuid().ToString("N")));
		try
		{
			if (source is not null) await Task.Run(() => CopyDirectory(source, stage, skipDefinition: true));
			instance.DirectoryPath = stage;
			Directory.CreateDirectory(instance.ModsPath);
			Directory.CreateDirectory(Path.Combine(stage, ".orbit"));
			await AtomicJsonFile.SaveAsync(Path.Combine(stage, DefinitionFile), instance);
			var destination = UniqueInstancePath(instance.Name, instancesRoot: _instancesRoot);
			Directory.Move(stage, destination);
			instance.DirectoryPath = destination;
			return instance;
		}
		catch
		{
			if (Path.GetDirectoryName(stage) == _instancesRoot && Path.GetFileName(stage).StartsWith(".orbit-new-", StringComparison.Ordinal)
				&& Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
			instance.DirectoryPath = string.Empty;
			throw;
		}
	}

	public Task SaveAsync(GameInstance instance) => LockedAsync(async () => { await SaveCoreAsync(instance); return true; });
	private async Task SaveCoreAsync(GameInstance instance)
	{
		if (string.IsNullOrWhiteSpace(instance.DirectoryPath))
		{
			throw new InvalidOperationException("У сборки не указан каталог.");
		}
		if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(instance.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar)), _instancesRoot, StringComparison.OrdinalIgnoreCase)
			|| (Directory.Exists(instance.DirectoryPath) && (File.GetAttributes(instance.DirectoryPath) & FileAttributes.ReparsePoint) != 0))
			throw new IOException("Сборка находится вне папки instances.");
		if (!Directory.Exists(instance.DirectoryPath))
			throw new DirectoryNotFoundException("Каталог сборки не найден.");
		await Task.Run(delegate
		{
			EnsureFriendlyDirectory(instance, _instancesRoot);
		});
		string destination = Path.Combine(instance.DirectoryPath, "instance.json");
		await AtomicJsonFile.SaveAsync(destination, instance);
	}

	public Task DeleteAsync(GameInstance instance) => LockedAsync(async () => { await DeleteCoreAsync(instance); return true; });
	private async Task DeleteCoreAsync(GameInstance instance)
	{
		var directory = Path.GetFullPath(instance.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar);
		if (!string.Equals(Path.GetDirectoryName(directory), _instancesRoot, StringComparison.OrdinalIgnoreCase)
			|| (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0))
			throw new IOException("Удаление разрешено только для сборки внутри папки instances.");
		if (!Directory.Exists(instance.DirectoryPath))
		{
			return;
		}
		for (int attempt = 1; attempt <= 4; attempt++)
		{
			try
			{
				await Task.Run(delegate
				{
					Directory.Delete(instance.DirectoryPath, recursive: true);
				});
				break;
			}
			catch (IOException) when (attempt < 4)
			{
				await Task.Delay(150 * attempt);
			}
			catch (UnauthorizedAccessException) when (attempt < 4)
			{
				await Task.Delay(150 * attempt);
			}
		}
	}

	public Task<GameInstance> DuplicateAsync(GameInstance source) => LockedAsync(async () =>
	{
		GameInstance clone = source.CloneDefinition();
		clone.Name = source.Name + " — копия";
		clone.IconFile = source.IconFile;
		return await CommitNewAsync(clone, source.DirectoryPath);
	});

	public Task ExportAsync(GameInstance instance, string zipPath)
	{
		return Task.Run(() => InstanceArchive.Export(instance.DirectoryPath, zipPath));
	}

	public Task<GameInstance> ImportAsync(string zipPath) => LockedAsync(() => ImportCoreAsync(zipPath));
	private async Task<GameInstance> ImportCoreAsync(string zipPath)
	{
		string temporary = Path.Combine(Path.GetTempPath(), "OrbitLauncher", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temporary);
		try
		{
			await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, temporary));
			string? definition = Directory.EnumerateFiles(temporary, "instance.json", SearchOption.AllDirectories).FirstOrDefault();
			GameInstance instance;
			string contentRoot;
			if (definition != null)
			{
				instance = AtomicJsonFile.Read<GameInstance>(definition) ?? throw new InvalidDataException("ZIP содержит пустое описание сборки.");
				contentRoot = Path.GetDirectoryName(definition) ?? throw new InvalidDataException("ZIP содержит некорректное описание сборки.");
			}
			else
			{
				instance = new GameInstance
				{
					Name = Path.GetFileNameWithoutExtension(zipPath)
				};
				contentRoot = temporary;
			}
			GameInstance imported = instance.CloneDefinition();
			imported.Name = instance.Name + " — импорт";
			imported.IconFile = ImportedIconPath(contentRoot, instance.IconFile);
			imported.JavaPath = string.Empty;
			imported.LaunchVersionId = string.Empty;
			return await CommitNewAsync(imported, contentRoot!);
		}
		finally
		{
			if (Directory.Exists(temporary))
			{
				var resolved = Path.GetFullPath(temporary);
				var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OrbitLauncher"));
				if (string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase)
					&& Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
				{
					try { await Task.Run(() => Directory.Delete(resolved, recursive: true)); }
					catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
			}
		}
	}

	private static void CopyDirectory(string source, string destination, bool skipDefinition)
	{
		InstanceArchive.Copy(source, destination, skipDefinition);
	}

	private static string ImportedIconPath(string contentRoot, string? icon)
	{
		if (string.IsNullOrWhiteSpace(icon) || Path.IsPathRooted(icon)) return string.Empty;
		try
		{
			var root = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var path = Path.GetFullPath(Path.Combine(root, icon));
			return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
				? Path.GetRelativePath(root, path) : string.Empty;
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return string.Empty; }
	}

	private static void EnsureFriendlyDirectory(GameInstance instance, string? instancesRoot = null)
	{
		if (string.IsNullOrWhiteSpace(instance.DirectoryPath) || !Directory.Exists(instance.DirectoryPath))
		{
			return;
		}
		string text = Path.GetFullPath(instance.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar);
		string b = SafeDirectoryName(instance.Name);
		if (string.Equals(Path.GetFileName(text), b, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string text2 = UniqueInstancePath(instance.Name, text, instancesRoot);
		if (string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			Directory.Move(text, text2);
			instance.DirectoryPath = text2;
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static string UniqueInstancePath(string name, string? currentPath = null, string? instancesRoot = null)
	{
		string text = SafeDirectoryName(name);
		for (int i = 1; i < 10000; i++)
		{
			string path = ((i == 1) ? text : $"{text} ({i})");
			string fullPath = Path.GetFullPath(Path.Combine(instancesRoot ?? AppPaths.Instances, path));
			if (currentPath != null && string.Equals(fullPath, currentPath, StringComparison.OrdinalIgnoreCase))
			{
				return fullPath;
			}
			if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
			{
				return fullPath;
			}
		}
		return Path.Combine(instancesRoot ?? AppPaths.Instances, $"{text}-{Guid.NewGuid():N}".Substring(0, Math.Min(text.Length + 9, 88)));
	}

	private static string SafeDirectoryName(string name)
	{
		// The Windows Java launcher converts argv through the system ANSI code page.
		// Keep physical directory names ASCII; the display name stays unchanged.
		const string cyrillic = "абвгдеёжзийклмнопрстуфхцчшщъыьэюяіїєґ";
		string[] latin = { "a", "b", "v", "g", "d", "e", "yo", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya", "i", "yi", "ye", "g" };
		var directoryName = new StringBuilder();
		foreach (char character in name.Trim().Normalize(NormalizationForm.FormD))
		{
			int index = cyrillic.IndexOf(char.ToLowerInvariant(character));
			if (index >= 0)
			{
				string value = latin[index];
				if (char.IsUpper(character) && value.Length > 0) value = char.ToUpperInvariant(value[0]) + value.Substring(1);
				directoryName.Append(value);
			}
			else if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-' or '_' or '(' or ')' or '.')
			{
				directoryName.Append(character);
			}
			else if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
			{
				directoryName.Append(' ');
			}
		}
		string text = directoryName.ToString();
		text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
		if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\.orbit-new-[0-9a-f]{32}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) text = "_" + text;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "Instance";
		}
		if (text.Length > 80)
		{
			text = text.Substring(0, 80).TrimEnd(' ', '.');
		}
		if (!new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
			"COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
			"LPT8", "LPT9"
		}.Contains(text.Split('.')[0].TrimEnd(' ')))
		{
			return text;
		}
		return "_" + text.Substring(0, Math.Min(text.Length, 79)).TrimEnd(' ', '.');
	}

	private static void MigrateSharedMinecraftFiles(GameInstance instance, string sharedRoot)
	{
		Directory.CreateDirectory(sharedRoot);
		string[] sharedMinecraftDirectories = SharedMinecraftDirectories;
		foreach (string path in sharedMinecraftDirectories)
		{
			string text = Path.Combine(instance.DirectoryPath, path);
			if (Directory.Exists(text))
			{
				string destination = Path.Combine(sharedRoot, path);
				MergeDirectory(text, destination);
			}
		}
	}

	private static void MergeDirectory(string source, string destination)
	{
		if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Перенос файлов по ссылке остановлен: " + source);
		if (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("Общий каталог является ссылкой: " + destination);
		Directory.CreateDirectory(destination);
		foreach (string item in Directory.EnumerateDirectories(source).ToList())
		{
			MergeDirectory(item, Path.Combine(destination, Path.GetFileName(item)));
		}
		foreach (string item2 in Directory.EnumerateFiles(source).ToList())
		{
			if ((File.GetAttributes(item2) & FileAttributes.ReparsePoint) != 0) throw new IOException("Перенос файла по ссылке остановлен: " + item2);
			string text = Path.Combine(destination, Path.GetFileName(item2));
			try
			{
				if (!File.Exists(text))
				{
					File.Move(item2, text);
					continue;
				}
				FileInfo fileInfo = new FileInfo(item2);
				FileInfo fileInfo2 = new FileInfo(text);
				// Different bytes are a conflict, even if lengths and timestamps match.
				// Retain both originals instead of silently discarding an instance's copy.
				if (fileInfo.Length == fileInfo2.Length)
				{
					bool identical;
					using (var left = File.OpenRead(item2))
					using (var right = File.OpenRead(text))
						identical = System.Security.Cryptography.SHA256.HashData(left).SequenceEqual(System.Security.Cryptography.SHA256.HashData(right));
					if (identical) File.Delete(item2);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
		try
		{
			if (!Directory.EnumerateFileSystemEntries(source).Any())
			{
				Directory.Delete(source);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
