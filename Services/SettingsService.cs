using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class SettingsService
{
	public async Task<LauncherSettings> LoadAsync()
	{
		AppPaths.EnsureCreated();
		if (!File.Exists(AppPaths.SettingsFile))
		{
			return new LauncherSettings();
		}
		try
		{
			LauncherSettings result;
			await using (FileStream stream = File.OpenRead(AppPaths.SettingsFile))
			{
				result = (await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonDefaults.Options)) ?? new LauncherSettings();
			}
			return result;
		}
		catch
		{
			return new LauncherSettings();
		}
	}

	public async Task SaveAsync(LauncherSettings settings)
	{
		AppPaths.EnsureCreated();
		string temporary = AppPaths.SettingsFile + ".tmp";
		await using (FileStream stream = File.Create(temporary))
		{
			await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options);
		}
		File.Move(temporary, AppPaths.SettingsFile, overwrite: true);
	}
}
