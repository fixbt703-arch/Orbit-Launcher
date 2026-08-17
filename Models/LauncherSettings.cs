using System.Text.Json.Serialization;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class LauncherSettings
{
    [JsonIgnore] public string MicrosoftClientId => AppConfiguration.MicrosoftClientId;
    [JsonIgnore] public string MinecraftManifestApi => AppConfiguration.MinecraftManifestApi;
    [JsonIgnore] public string ModrinthApiBase => AppConfiguration.ModrinthApiBase;
    [JsonIgnore] public string CurseForgeApiBase => AppConfiguration.CurseForgeApiBase;
    [JsonIgnore] public string CurseForgeApiKey => AppConfiguration.CurseForgeApiKey;
    [JsonIgnore] public string FabricMetaApi => AppConfiguration.FabricMetaApi;
    [JsonIgnore] public string QuiltMetaApi => AppConfiguration.QuiltMetaApi;
    [JsonIgnore] public string ForgeMavenMetadataApi => AppConfiguration.ForgeMavenMetadataApi;
    [JsonIgnore] public string NeoForgeMavenMetadataApi => AppConfiguration.NeoForgeMavenMetadataApi;
    public int DefaultMinimumRamMb { get; set; } = 1024;
    public int DefaultMaximumRamMb { get; set; } = 4096;
    public bool CloseLauncherOnGameStart { get; set; }
    public bool ShowSnapshots { get; set; }
    public bool OfflineModeEnabled { get; set; }
    public string OfflinePlayerName { get; set; } = "OrbitPlayer";
    public string ModDependencyDownloadMode { get; set; } = "required";
    public bool AutoInstallFabricApi { get; set; } = true;
    public string Language { get; set; } = "ru";
}
