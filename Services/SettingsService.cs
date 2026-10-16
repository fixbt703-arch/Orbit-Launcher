using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class SettingsService
{
	private readonly string _path;
	public SettingsService(string? path = null) => _path = path ?? AppPaths.SettingsFile;
	public Task<LauncherSettings> LoadAsync()
	{
		try
		{
			return Task.FromResult(AtomicJsonFile.Read<LauncherSettings>(_path) ?? new LauncherSettings());
		}
		catch (JsonException)
		{
			return Task.FromResult(new LauncherSettings());
		}
	}

	public Task SaveAsync(LauncherSettings settings) => AtomicJsonFile.SaveAsync(_path, settings);
}
