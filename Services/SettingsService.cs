using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class SettingsService
{
    public async Task<LauncherSettings> LoadAsync()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsFile))
            return new LauncherSettings();

        try
        {
            await using var stream = File.OpenRead(AppPaths.SettingsFile);
            return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonDefaults.Options)
                   ?? new LauncherSettings();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        AppPaths.EnsureCreated();
        var temporary = AppPaths.SettingsFile + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options);

        File.Move(temporary, AppPaths.SettingsFile, true);
    }
}
