using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class InstanceRepository
{
	private const string DefinitionFile = "instance.json";

	private static readonly string[] SharedMinecraftDirectories = new string[5] { "assets", "libraries", "runtime", "versions", "resources" };

	public async Task<IReadOnlyList<GameInstance>> GetAllAsync()
	{
		AppPaths.EnsureCreated();
		List<GameInstance> instances = new List<GameInstance>();
		foreach (string directory in Directory.EnumerateDirectories(AppPaths.Instances).ToList())
		{
			string path = Path.Combine(directory, "instance.json");
			if (!File.Exists(path))
			{
				continue;
			}
			try
			{
				GameInstance instance;
				await using (FileStream stream = File.OpenRead(path))
				{
					instance = await JsonSerializer.DeserializeAsync<GameInstance>(stream, JsonDefaults.Options);
				}
				if (instance != null)
				{
					instance.DirectoryPath = directory;
					await Task.Run(delegate
					{
						EnsureFriendlyDirectory(instance);
					});
					await Task.Run(delegate
					{
						MigrateSharedMinecraftFiles(instance);
					});
					Directory.CreateDirectory(instance.ModsPath);
					instances.Add(instance);
				}
			}
			catch
			{
			}
		}
		return (from x in instances
			orderby x.LastPlayedAt ?? x.CreatedAt descending, x.Name
			select x).ToList();
	}

	public async Task<GameInstance> CreateAsync(GameInstance instance)
	{
		AppPaths.EnsureCreated();
		instance.Id = Guid.NewGuid().ToString("N");
		instance.DirectoryPath = UniqueInstancePath(instance.Name);
		Directory.CreateDirectory(instance.DirectoryPath);
		Directory.CreateDirectory(instance.ModsPath);
		Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, ".orbit"));
		await SaveAsync(instance);
		return instance;
	}

	public async Task SaveAsync(GameInstance instance)
	{
		if (string.IsNullOrWhiteSpace(instance.DirectoryPath))
		{
			throw new InvalidOperationException("У сборки не указан каталог.");
		}
		await Task.Run(delegate
		{
			EnsureFriendlyDirectory(instance);
		});
		Directory.CreateDirectory(instance.DirectoryPath);
		string destination = Path.Combine(instance.DirectoryPath, "instance.json");
		string temporary = destination + ".tmp";
		await using (FileStream stream = File.Create(temporary))
		{
			await JsonSerializer.SerializeAsync(stream, instance, JsonDefaults.Options);
		}
		File.Move(temporary, destination, overwrite: true);
	}

	public async Task DeleteAsync(GameInstance instance)
	{
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

	public async Task<GameInstance> DuplicateAsync(GameInstance source)
	{
		GameInstance clone = source.CloneDefinition();
		clone.Name = source.Name + " — копия";
		await CreateAsync(clone);
		CopyDirectory(source.DirectoryPath, clone.DirectoryPath, skipDefinition: true);
		clone.IconFile = source.IconFile;
		await SaveAsync(clone);
		return clone;
	}

	public Task ExportAsync(GameInstance instance, string zipPath)
	{
		if (File.Exists(zipPath))
		{
			File.Delete(zipPath);
		}
		ZipFile.CreateFromDirectory(instance.DirectoryPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
		return Task.CompletedTask;
	}

	public async Task<GameInstance> ImportAsync(string zipPath)
	{
		string temporary = Path.Combine(Path.GetTempPath(), "OrbitLauncher", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temporary);
		try
		{
			ZipFile.ExtractToDirectory(zipPath, temporary);
			string definition = Directory.EnumerateFiles(temporary, "instance.json", SearchOption.AllDirectories).FirstOrDefault();
			GameInstance instance;
			string contentRoot;
			if (definition != null)
			{
				await using FileStream stream = File.OpenRead(definition);
				instance = (await JsonSerializer.DeserializeAsync<GameInstance>(stream, JsonDefaults.Options)) ?? new GameInstance();
				contentRoot = Path.GetDirectoryName(definition);
			}
			else
			{
				instance = new GameInstance
				{
					Name = Path.GetFileNameWithoutExtension(zipPath)
				};
				contentRoot = temporary;
			}
			GameInstance imported = await CreateAsync(instance.CloneDefinition());
			imported.Name = instance.Name + " — импорт";
			CopyDirectory(contentRoot, imported.DirectoryPath, skipDefinition: true);
			imported.IconFile = instance.IconFile;
			await SaveAsync(imported);
			return imported;
		}
		finally
		{
			if (Directory.Exists(temporary))
			{
				Directory.Delete(temporary, recursive: true);
			}
		}
	}

	private static void CopyDirectory(string source, string destination, bool skipDefinition)
	{
		Directory.CreateDirectory(destination);
		foreach (string item in Directory.EnumerateFiles(source))
		{
			if (!skipDefinition || !string.Equals(Path.GetFileName(item), "instance.json", StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(item, Path.Combine(destination, Path.GetFileName(item)), overwrite: true);
			}
		}
		foreach (string item2 in Directory.EnumerateDirectories(source))
		{
			CopyDirectory(item2, Path.Combine(destination, Path.GetFileName(item2)), skipDefinition: false);
		}
	}

	private static void EnsureFriendlyDirectory(GameInstance instance)
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
		string text2 = UniqueInstancePath(instance.Name, text);
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

	private static string UniqueInstancePath(string name, string? currentPath = null)
	{
		string text = SafeDirectoryName(name);
		for (int i = 1; i < 10000; i++)
		{
			string path = ((i == 1) ? text : $"{text} ({i})");
			string fullPath = Path.GetFullPath(Path.Combine(AppPaths.Instances, path));
			if (currentPath != null && string.Equals(fullPath, currentPath, StringComparison.OrdinalIgnoreCase))
			{
				return fullPath;
			}
			if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
			{
				return fullPath;
			}
		}
		return Path.Combine(AppPaths.Instances, $"{text}-{Guid.NewGuid():N}".Substring(0, Math.Min(text.Length + 9, 88)));
	}

	private static string SafeDirectoryName(string name)
	{
		HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
		string text = new string((from character in name.Trim()
			select (!invalid.Contains(character)) ? character : '_').ToArray());
		text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "Сборка";
		}
		if (text.Length > 80)
		{
			text = text.Substring(0, 80).TrimEnd();
		}
		if (!new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
			"COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
			"LPT8", "LPT9"
		}.Contains(text))
		{
			return text;
		}
		return "_" + text;
	}

	private static void MigrateSharedMinecraftFiles(GameInstance instance)
	{
		Directory.CreateDirectory(AppPaths.Minecraft);
		string[] sharedMinecraftDirectories = SharedMinecraftDirectories;
		foreach (string path in sharedMinecraftDirectories)
		{
			string text = Path.Combine(instance.DirectoryPath, path);
			if (Directory.Exists(text))
			{
				string destination = Path.Combine(AppPaths.Minecraft, path);
				MergeDirectory(text, destination);
			}
		}
	}

	private static void MergeDirectory(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string item in Directory.EnumerateDirectories(source).ToList())
		{
			MergeDirectory(item, Path.Combine(destination, Path.GetFileName(item)));
		}
		foreach (string item2 in Directory.EnumerateFiles(source).ToList())
		{
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
				if (fileInfo.Length != fileInfo2.Length && fileInfo.LastWriteTimeUtc > fileInfo2.LastWriteTimeUtc)
				{
					File.Copy(item2, text, overwrite: true);
				}
				File.Delete(item2);
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
