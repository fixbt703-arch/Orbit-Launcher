using System.Text.Json.Serialization;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class LauncherSettings
{
	[JsonIgnore]
	public string MicrosoftClientId => AppConfiguration.MicrosoftClientId;

	[JsonIgnore]
	public string MinecraftManifestApi => "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

	[JsonIgnore]
	public string ModrinthApiBase => "https://api.modrinth.com/v2";

	[JsonIgnore]
	public string CurseForgeApiBase => "https://api.curseforge.com/v1";

	[JsonIgnore]
	public string CurseForgeApiKey => AppConfiguration.CurseForgeApiKey;

	[JsonIgnore]
	public string FabricMetaApi => "https://meta.fabricmc.net/v2";

	[JsonIgnore]
	public string QuiltMetaApi => "https://meta.quiltmc.org/v3";

	[JsonIgnore]
	public string ForgeMavenMetadataApi => "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

	[JsonIgnore]
	public string NeoForgeMavenMetadataApi => "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

	public int DefaultMinimumRamMb { get; set; } = 1024;

	public int DefaultMaximumRamMb { get; set; } = 4096;

	public bool CloseLauncherOnGameStart { get; set; }

	public bool ShowSnapshots { get; set; }

	public bool OfflineModeEnabled { get; set; }

	public string OfflinePlayerName { get; set; } = "OrbitPlayer";

	public string ModDependencyDownloadMode { get; set; } = "required";

	public bool AutoInstallFabricApi { get; set; } = true;

	public string Language { get; set; } = "ru";

	public string LauncherBackground { get; set; } = "default";

	public int BackgroundDimmingPercent { get; set; } = 65;

	public int FrameTransparencyPercent { get; set; } = 70;
}
