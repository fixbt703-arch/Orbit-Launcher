using OrbitLauncher.Utilities;
using System.Windows.Media;

namespace OrbitLauncher.Models;

public sealed class ModSearchResult : ObservableObject
{
    private ImageSource? _icon;

    public string ProjectId { get; init; } = string.Empty;
    public string SourceId { get; init; } = "modrinth";
    public string WebsiteUrl { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string IconUrl { get; init; } = string.Empty;
    public string ProjectType { get; init; } = "mod";
    public string LatestVersion { get; init; } = string.Empty;
    public DateTime? DateModified { get; init; }
    public long Downloads { get; init; }
    public ImageSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }
    public string ProjectTypeDisplay => ProjectType switch
    {
        "modpack" => "Модпак",
        "resourcepack" => "Ресурспак",
        "shader" => "Шейдер",
        "datapack" => "Датапак",
        _ => "Мод"
    };
    public string ActionText => ProjectType is "modpack" or "datapack" ? "Скачать" : "Установить";
    public string DownloadsText => Downloads switch
    {
        >= 1_000_000 => $"{Downloads / 1_000_000d:0.#} млн загрузок",
        >= 1_000 => $"{Downloads / 1_000d:0.#} тыс. загрузок",
        _ => $"{Downloads} загрузок"
    };
    public string UpdatedText => DateModified is null ? string.Empty : $"Обновлён {DateModified.Value.ToLocalTime():dd.MM.yyyy}";
}

public sealed record ModSearchOption(string Id, string DisplayName);

public sealed record ModCategoryOption(string Id, string DisplayName);

public sealed record ModrinthSearchRequest(
    string Query,
    string ProjectType,
    string GameVersion,
    string Loader,
    string Category,
    string Environment,
    string Index,
    int Offset = 0,
    int Limit = 50);

public sealed record ModrinthSearchResponse(IReadOnlyList<ModSearchResult> Items, int TotalHits);

public sealed class ModProjectDetails
{
    public string ProjectId { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string WebsiteUrl { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string BodyText { get; init; } = string.Empty;
    public string ProjectType { get; init; } = "mod";
    public string Author { get; init; } = string.Empty;
    public string IconUrl { get; init; } = string.Empty;
    public long Downloads { get; init; }
    public long Followers { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string License { get; init; } = "Не указана";
    public string SourceUrl { get; init; } = string.Empty;
    public string IssuesUrl { get; init; } = string.Empty;
    public string VersionName { get; init; } = string.Empty;
    public IReadOnlyList<string> GameVersions { get; init; } = [];
    public IReadOnlyList<string> Loaders { get; init; } = [];
    public IReadOnlyList<ModDependencyInfo> Dependencies { get; init; } = [];
    public IReadOnlyList<ModGalleryImage> Gallery { get; init; } = [];
    public ModSearchResult SearchResult { get; init; } = new();
    public string StatsText => $"{FormatCompact(Downloads)} загрузок  •  {FormatCompact(Followers)} подписчиков";
    public string CompatibilityText => VersionName.Length == 0
        ? "Для выбранных фильтров совместимая версия не найдена"
        : $"Версия: {VersionName}";
    public string UpdatedText => UpdatedAt is null ? string.Empty : $"Обновлён {UpdatedAt.Value.ToLocalTime():dd.MM.yyyy}";
    public string ActionText => SearchResult.ActionText;
    public string AuthorText => string.IsNullOrWhiteSpace(Author) ? "Автор не указан" : $"Автор: {Author}";
    public string LicenseText => $"Лицензия: {License}";
    public string DependencyStatusText
    {
        get
        {
            var required = Dependencies.Count(item => string.Equals(item.Type, "required", StringComparison.OrdinalIgnoreCase));
            var optional = Dependencies.Count(item => string.Equals(item.Type, "optional", StringComparison.OrdinalIgnoreCase));
            var version = string.IsNullOrWhiteSpace(VersionName) ? "выбранной версии" : $"версии {VersionName}";
            return Dependencies.Count == 0
                ? $"Для {version} зависимости не указаны."
                : $"Для {version}: обязательных — {required}, необязательных — {optional}.";
        }
    }

    private static string FormatCompact(long value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000d:0.#} млн",
        >= 1_000 => $"{value / 1_000d:0.#} тыс.",
        _ => value.ToString()
    };
}

public sealed class ModGalleryImage : ObservableObject
{
    private ImageSource? _image;
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ImageSource? Image { get => _image; set => SetProperty(ref _image, value); }
}

public sealed record ModDependencyInfo(string ProjectId, string Title, string Type, string TypeDisplay);

public sealed record ModDownloadResult(string DestinationPath, string Message, bool InstalledDirectly);

public sealed class InstalledMod
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public long SizeBytes { get; init; }
    public string TypeText { get; init; } = "Мод";
    public DateTime ModifiedAt { get; init; }
    public string FolderText { get; init; } = string.Empty;
    public bool CanToggle { get; init; } = true;
    public string StateText => IsEnabled ? "Включён" : "Отключён";
    public string ToggleActionText => IsEnabled ? "Выключить" : "Включить";
    public string ModifiedText => $"Изменён {ModifiedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
    public string IconGlyph => TypeText switch
    {
        "Ресурспак" => "\uE8B9",
        "Шейдер" => "\uE706",
        "Скачанный набор" => "\uE896",
        _ => "\uE74C"
    };
    public string SizeText => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024d / 1024d:0.0} МБ"
        : $"{Math.Max(1, SizeBytes / 1024)} КБ";
}

public sealed class DownloadItem : ObservableObject
{
    private double _progress;
    private string _status = "Ожидание";

    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}

public sealed record LaunchProgress(double Percent, string Status);
public sealed record LaunchResult(string VersionId, int ProcessId);
