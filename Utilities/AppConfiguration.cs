namespace OrbitLauncher.Utilities;

/// <summary>
/// Build-time service configuration. Public endpoints live here; credentials are
/// supplied by the git-ignored AppConfiguration.Local.cs partial declaration.
/// </summary>
internal static partial class AppConfiguration
{
    private sealed class Values
    {
        public string MicrosoftClientId { get; set; } = string.Empty;
        public string CurseForgeApiKey { get; set; } = string.Empty;
    }

    public static string MicrosoftClientId { get; }
    public static string CurseForgeApiKey { get; }

    public const string MinecraftManifestApi = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string ModrinthApiBase = "https://api.modrinth.com/v2";
    public const string CurseForgeApiBase = "https://api.curseforge.com/v1";
    public const string FabricMetaApi = "https://meta.fabricmc.net/v2";
    public const string QuiltMetaApi = "https://meta.quiltmc.org/v3";
    public const string ForgeMavenMetadataApi = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    public const string NeoForgeMavenMetadataApi = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    public const string FabricApiProjectId = "P7dR8mSH";

    static AppConfiguration()
    {
        var values = new Values();
        ApplyLocal(values);
        MicrosoftClientId = values.MicrosoftClientId.Trim();
        CurseForgeApiKey = values.CurseForgeApiKey.Trim();
    }

    static partial void ApplyLocal(Values values);
}
