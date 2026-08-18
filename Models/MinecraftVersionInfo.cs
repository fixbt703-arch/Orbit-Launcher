using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class MinecraftVersionInfo
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = "release";
    public DateTime ReleaseTime { get; init; }
    public string DisplayName => Type == "release" ? Id : $"{Id}  ({Type})";
    public string TypeDisplay => LocalizationService.Translate(Type switch
    {
        "release" => "Релиз",
        "snapshot" => "Снапшот",
        "old_beta" => "Beta",
        "old_alpha" => "Alpha",
        _ => Type
    });
}
