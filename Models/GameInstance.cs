using System.Text.Json.Serialization;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class GameInstance : ObservableObject
{
    private string _name = "Новая сборка";
    private string _gameVersion = "1.21.1";
    private string _loader = "Vanilla";
    private string _loaderVersion = string.Empty;
    private string _launchVersionId = string.Empty;
    private int _minimumRamMb = 1024;
    private int _maximumRamMb = 4096;
    private string _javaPath = string.Empty;
    private string _iconFile = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string GameVersion { get => _gameVersion; set => SetProperty(ref _gameVersion, value); }
    public string Loader { get => _loader; set => SetProperty(ref _loader, value); }
    public string LoaderVersion { get => _loaderVersion; set => SetProperty(ref _loaderVersion, value); }
    public string LaunchVersionId { get => _launchVersionId; set => SetProperty(ref _launchVersionId, value); }
    public int MinimumRamMb { get => _minimumRamMb; set => SetProperty(ref _minimumRamMb, value); }
    public int MaximumRamMb { get => _maximumRamMb; set => SetProperty(ref _maximumRamMb, value); }
    public string JavaPath { get => _javaPath; set => SetProperty(ref _javaPath, value); }
    public string IconFile { get => _iconFile; set => SetProperty(ref _iconFile, value); }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedAt { get; set; }
    public long TotalPlaySeconds { get; set; }

    [JsonIgnore] public string DirectoryPath { get; set; } = string.Empty;
    [JsonIgnore] public string ModsPath => Path.Combine(DirectoryPath, "mods");
    [JsonIgnore] public string IconPath => string.IsNullOrWhiteSpace(IconFile) ? string.Empty : Path.Combine(DirectoryPath, IconFile);
    [JsonIgnore] public string LoaderDisplay => string.IsNullOrWhiteSpace(LoaderVersion) || Loader == "Vanilla"
        ? Loader
        : $"{Loader} {LoaderVersion}";
    [JsonIgnore] public string Subtitle => $"Minecraft {GameVersion}  •  {LoaderDisplay}";
    [JsonIgnore] public string LastPlayedText => LastPlayedAt is null
        ? LocalizationService.IsEnglish ? "Never played" : "Ещё не запускалась"
        : LocalizationService.IsEnglish ? $"Played: {LastPlayedAt.Value.ToLocalTime():dd.MM.yyyy}" : $"Запуск: {LastPlayedAt.Value.ToLocalTime():dd.MM.yyyy}";
    [JsonIgnore] public string LoaderBadge => Loader.ToUpperInvariant();
    [JsonIgnore] public int ModCount => Directory.Exists(ModsPath)
        ? Directory.EnumerateFiles(ModsPath).Count(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        : 0;
    [JsonIgnore] public string ModCountText => ModCount == 0
        ? LocalizationService.Translate("Без модов")
        : LocalizationService.IsEnglish ? $"Mods: {ModCount}" : $"Модов: {ModCount}";

    public GameInstance CloneDefinition() => new()
    {
        Name = Name,
        GameVersion = GameVersion,
        Loader = Loader,
        LoaderVersion = LoaderVersion,
        LaunchVersionId = LaunchVersionId,
        MinimumRamMb = MinimumRamMb,
        MaximumRamMb = MaximumRamMb,
        JavaPath = JavaPath,
        CreatedAt = DateTime.UtcNow
    };
}
