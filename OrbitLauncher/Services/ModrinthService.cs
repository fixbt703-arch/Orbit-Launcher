using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

public sealed class ModrinthService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBase;

    public ModrinthService(HttpClient httpClient, string? apiBase = null)
    {
        _httpClient = httpClient;
        _apiBase = string.IsNullOrWhiteSpace(apiBase) ? "https://api.modrinth.com/v2" : apiBase.TrimEnd('/');
    }

    public async Task<IReadOnlyList<ModSearchOption>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync($"{_apiBase}/tag/game_version", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Where(item => item.GetProperty("version_type").GetString() == "release")
            .Select(item => item.GetProperty("version").GetString() ?? string.Empty)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => new ModSearchOption(version, version))
            .ToList();
    }

    public async Task<IReadOnlyList<ModSearchOption>> GetLoadersAsync(string projectType, CancellationToken cancellationToken = default)
    {
        if (projectType == "datapack")
            return [new ModSearchOption("datapack", "Data Pack")];

        using var document = await GetJsonAsync($"{_apiBase}/tag/loader", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Where(item => item.TryGetProperty("supported_project_types", out var types)
                           && types.EnumerateArray().Any(type => type.GetString() == projectType))
            .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => projectType != "mod" || name is "neoforge" or "forge" or "fabric" or "quilt")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(LoaderOrder)
            .ThenBy(name => name)
            .Select(name => new ModSearchOption(name, LoaderDisplayName(name)))
            .ToList();
    }

    public async Task<IReadOnlyList<ModCategoryOption>> GetCategoriesAsync(string projectType, CancellationToken cancellationToken = default)
    {
        var apiProjectType = projectType == "datapack" ? "mod" : projectType;
        using var document = await GetJsonAsync($"{_apiBase}/tag/category", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Where(item => item.TryGetProperty("project_type", out var type) && type.GetString() == apiProjectType)
            .Where(item => item.TryGetProperty("header", out var header) && header.GetString() == "categories")
            .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name != "datapack")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CategoryDisplayName)
            .Select(name => new ModCategoryOption(name, CategoryDisplayName(name)))
            .ToList();
    }

    public async Task<ModrinthSearchResponse> SearchAsync(ModrinthSearchRequest request, CancellationToken cancellationToken = default)
    {
        var apiProjectType = request.ProjectType == "datapack" ? "mod" : request.ProjectType;
        var facets = new List<string[]> { new[] { $"project_type:{apiProjectType}" } };
        if (request.ProjectType == "datapack")
            facets.Add(new[] { "categories:datapack" });
        if (!string.IsNullOrWhiteSpace(request.GameVersion))
            facets.Add(new[] { $"versions:{request.GameVersion}" });
        if (!string.IsNullOrWhiteSpace(request.Loader))
            facets.Add(new[] { $"categories:{request.Loader}" });
        if (!string.IsNullOrWhiteSpace(request.Category))
            facets.Add(new[] { $"categories:{request.Category}" });
        if (request.Environment == "client")
            facets.Add(new[] { "client_side!=unsupported" });
        else if (request.Environment == "server")
            facets.Add(new[] { "server_side!=unsupported" });

        var index = request.Index is "downloads" or "follows" or "newest" or "updated" ? request.Index : "relevance";
        var limit = Math.Clamp(request.Limit, 1, 100);
        var offset = Math.Max(0, request.Offset);
        var url = $"{_apiBase}/search?query={Uri.EscapeDataString(request.Query ?? string.Empty)}&limit={limit}&offset={offset}&index={index}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        using var document = await GetJsonAsync(url, cancellationToken);
        var items = document.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ModSearchResult
        {
            ProjectId = hit.GetProperty("project_id").GetString() ?? string.Empty,
            SourceId = "modrinth",
            Title = hit.GetProperty("title").GetString() ?? "Без названия",
            Description = hit.GetProperty("description").GetString() ?? string.Empty,
            Author = hit.TryGetProperty("author", out var author) ? author.GetString() ?? string.Empty : string.Empty,
            IconUrl = hit.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() ?? string.Empty : string.Empty,
            ProjectType = request.ProjectType == "datapack"
                ? "datapack"
                : hit.TryGetProperty("project_type", out var projectType) ? projectType.GetString() ?? request.ProjectType : request.ProjectType,
            LatestVersion = hit.TryGetProperty("latest_version", out var latest) && latest.ValueKind == JsonValueKind.String ? latest.GetString() ?? string.Empty : string.Empty,
            DateModified = hit.TryGetProperty("date_modified", out var modified) && modified.ValueKind == JsonValueKind.String ? modified.GetDateTime() : null,
            Downloads = hit.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : 0
        }).ToList();
        var totalHits = document.RootElement.TryGetProperty("total_hits", out var total) ? total.GetInt32() : items.Count;
        return new ModrinthSearchResponse(items, totalHits);
    }

    public async Task<ModProjectDetails> GetProjectDetailsAsync(
        ModSearchResult result,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        using var projectDocument = await GetJsonAsync($"{_apiBase}/project/{Uri.EscapeDataString(result.ProjectId)}", cancellationToken);
        var project = projectDocument.RootElement;
        using var versionsDocument = await GetVersionsAsync(result.ProjectId, gameVersion, EffectiveLoader(result.ProjectType, loader), cancellationToken);
        var version = versionsDocument.RootElement.GetArrayLength() > 0 ? versionsDocument.RootElement[0] : default;
        var dependencies = version.ValueKind == JsonValueKind.Object
            ? await ResolveDependenciesAsync(version, cancellationToken)
            : [];

        var gallery = project.TryGetProperty("gallery", out var galleryElement)
            ? galleryElement.EnumerateArray()
                .OrderByDescending(item => item.TryGetProperty("featured", out var featured) && featured.GetBoolean())
                .ThenBy(item => item.TryGetProperty("ordering", out var ordering) ? ordering.GetInt32() : 0)
                .Select(item => new ModGalleryImage
                {
                    Url = item.GetProperty("url").GetString() ?? string.Empty,
                    Title = item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() ?? string.Empty : string.Empty,
                    Description = item.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String ? description.GetString() ?? string.Empty : string.Empty
                })
                .Where(image => !string.IsNullOrWhiteSpace(image.Url))
                .ToList()
            : [];

        return new ModProjectDetails
        {
            ProjectId = result.ProjectId,
            Slug = project.TryGetProperty("slug", out var slug) ? slug.GetString() ?? string.Empty : string.Empty,
            WebsiteUrl = $"https://modrinth.com/project/{(project.TryGetProperty("slug", out var webSlug) ? webSlug.GetString() ?? result.ProjectId : result.ProjectId)}",
            Title = project.TryGetProperty("title", out var title) ? title.GetString() ?? result.Title : result.Title,
            Summary = project.TryGetProperty("description", out var summary) ? summary.GetString() ?? result.Description : result.Description,
            BodyText = CleanMarkdown(project.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty),
            ProjectType = result.ProjectType,
            Author = result.Author,
            IconUrl = project.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() ?? result.IconUrl : result.IconUrl,
            Downloads = project.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : result.Downloads,
            Followers = project.TryGetProperty("followers", out var followers) ? followers.GetInt64() : 0,
            UpdatedAt = project.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.String ? updated.GetDateTime() : result.DateModified,
            License = project.TryGetProperty("license", out var license) && license.TryGetProperty("name", out var licenseName) ? licenseName.GetString() ?? "Не указана" : "Не указана",
            SourceUrl = project.TryGetProperty("source_url", out var source) && source.ValueKind == JsonValueKind.String ? source.GetString() ?? string.Empty : string.Empty,
            IssuesUrl = project.TryGetProperty("issues_url", out var issues) && issues.ValueKind == JsonValueKind.String ? issues.GetString() ?? string.Empty : string.Empty,
            VersionName = version.ValueKind == JsonValueKind.Object && version.TryGetProperty("version_number", out var versionName) ? versionName.GetString() ?? string.Empty : string.Empty,
            GameVersions = project.TryGetProperty("game_versions", out var gameVersions) ? gameVersions.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(value => value.Length > 0).ToList() : [],
            Loaders = project.TryGetProperty("loaders", out var loaders) ? loaders.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(value => value.Length > 0).ToList() : [],
            Dependencies = dependencies,
            Gallery = gallery,
            SearchResult = result
        };
    }

    public async Task<ModDownloadResult> DownloadAsync(
        ModSearchResult project,
        GameInstance instance,
        string selectedGameVersion,
        string selectedLoader,
        string dependencyDownloadMode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gameVersion = project.ProjectType == "modpack"
            ? selectedGameVersion.Length > 0 ? selectedGameVersion : instance.GameVersion
            : instance.GameVersion;
        var loader = project.ProjectType switch
        {
            "mod" => string.Equals(instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase) ? selectedLoader : instance.Loader.ToLowerInvariant(),
            "datapack" => "datapack",
            "resourcepack" => string.IsNullOrWhiteSpace(selectedLoader) ? "minecraft" : selectedLoader,
            "shader" => selectedLoader,
            "modpack" => selectedLoader,
            _ => selectedLoader
        };

        if (project.ProjectType == "mod" && string.IsNullOrWhiteSpace(loader))
            throw new InvalidOperationException("Выберите загрузчик модов или используйте сборку Fabric, Forge, NeoForge либо Quilt.");

        var destinationDirectory = DestinationDirectory(project.ProjectType, instance);
        Directory.CreateDirectory(destinationDirectory);

        if (project.ProjectType == "mod")
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mode = dependencyDownloadMode is "all" or "none" ? dependencyDownloadMode : "required";
            var installedPath = await InstallModProjectAsync(project.ProjectId, instance, loader, mode, visited, installed, progress, cancellationToken);
            var dependencyCount = Math.Max(0, installed.Count - 1);
            var dependencyText = dependencyCount == 0
                ? string.Empty
                : mode == "all"
                    ? $" Зависимостей установлено: {dependencyCount}."
                    : $" Обязательных зависимостей установлено: {dependencyCount}.";
            return new ModDownloadResult(installedPath, $"Мод установлен в сборку «{instance.Name}».{dependencyText}", true);
        }

        using var versionsDocument = await GetVersionsAsync(project.ProjectId, gameVersion, EffectiveLoader(project.ProjectType, loader), cancellationToken);
        if (versionsDocument.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Для выбранной версии Minecraft и загрузчика нет совместимого файла.");

        var path = await DownloadPrimaryFileAsync(versionsDocument.RootElement[0], destinationDirectory, project.ProjectId, progress, cancellationToken);
        var result = project.ProjectType switch
        {
            "resourcepack" => new ModDownloadResult(path, $"Ресурспак установлен в сборку «{instance.Name}».", true),
            "shader" => new ModDownloadResult(path, $"Шейдер установлен в сборку «{instance.Name}».", true),
            "datapack" => new ModDownloadResult(path, "Датапак скачан. Переместите ZIP в папку datapacks нужного мира.", false),
            "modpack" => new ModDownloadResult(path, "Модпак скачан в папку загрузок сборки.", false),
            _ => new ModDownloadResult(path, "Файл скачан.", false)
        };
        return result;
    }

    private async Task<string> InstallModProjectAsync(
        string projectId,
        GameInstance instance,
        string loader,
        string dependencyDownloadMode,
        HashSet<string> visited,
        HashSet<string> installed,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!visited.Add(projectId))
            return string.Empty;
        var completed = false;
        try
        {
            using var versionsDocument = await GetVersionsAsync(projectId, instance.GameVersion, loader, cancellationToken);
            if (versionsDocument.RootElement.GetArrayLength() == 0)
                throw new InvalidOperationException("У мода или одной из его зависимостей нет совместимой версии для выбранной сборки.");

            var version = versionsDocument.RootElement[0];
            if (dependencyDownloadMode != "none" && version.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateArray())
                {
                    var dependencyType = dependency.TryGetProperty("dependency_type", out var type) ? type.GetString() ?? string.Empty : string.Empty;
                    var shouldInstall = string.Equals(dependencyType, "required", StringComparison.OrdinalIgnoreCase)
                                        || dependencyDownloadMode == "all" && string.Equals(dependencyType, "optional", StringComparison.OrdinalIgnoreCase);
                    if (!shouldInstall)
                        continue;
                    var dependencyProjectId = await GetDependencyProjectIdAsync(dependency, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(dependencyProjectId))
                    {
                        try
                        {
                            await InstallModProjectAsync(dependencyProjectId, instance, loader, dependencyDownloadMode, visited, installed, null, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException
                                                   && string.Equals(dependencyType, "optional", StringComparison.OrdinalIgnoreCase))
                        {
                            // Необязательная зависимость не должна прерывать установку основного мода.
                        }
                    }
                }
            }

            Directory.CreateDirectory(instance.ModsPath);
            var path = await DownloadPrimaryFileAsync(version, instance.ModsPath, projectId, progress, cancellationToken);
            installed.Add(projectId);
            completed = true;
            return path;
        }
        finally
        {
            if (!completed)
                visited.Remove(projectId);
        }
    }

    private async Task<JsonDocument> GetVersionsAsync(string projectId, string gameVersion, string loader, CancellationToken cancellationToken)
    {
        var query = new List<string> { "include_changelog=false" };
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}");
        if (!string.IsNullOrWhiteSpace(loader))
            query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() }))}");
        return await GetJsonAsync($"{_apiBase}/project/{Uri.EscapeDataString(projectId)}/version?{string.Join('&', query)}", cancellationToken);
    }

    private async Task<IReadOnlyList<ModDependencyInfo>> ResolveDependenciesAsync(JsonElement version, CancellationToken cancellationToken)
    {
        if (!version.TryGetProperty("dependencies", out var dependenciesElement))
            return [];

        var raw = new List<(string ProjectId, string Title, string Type)>();
        foreach (var dependency in dependenciesElement.EnumerateArray())
        {
            var projectId = await GetDependencyProjectIdAsync(dependency, cancellationToken);
            var fileName = dependency.TryGetProperty("file_name", out var file) && file.ValueKind == JsonValueKind.String ? file.GetString() ?? string.Empty : string.Empty;
            var type = dependency.TryGetProperty("dependency_type", out var dependencyType) ? dependencyType.GetString() ?? "required" : "required";
            if (!string.IsNullOrWhiteSpace(projectId) || !string.IsNullOrWhiteSpace(fileName))
                raw.Add((projectId, fileName, type));
        }

        var projectIds = raw.Select(item => item.ProjectId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (projectIds.Count > 0)
        {
            using var projectsDocument = await GetJsonAsync($"{_apiBase}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(projectIds))}", cancellationToken);
            foreach (var project in projectsDocument.RootElement.EnumerateArray())
            {
                var id = project.GetProperty("id").GetString() ?? string.Empty;
                var title = project.GetProperty("title").GetString() ?? id;
                if (id.Length > 0)
                    titles[id] = title;
            }
        }

        return raw.Select(item => new ModDependencyInfo(
                item.ProjectId,
                titles.TryGetValue(item.ProjectId, out var title) ? title : item.Title.Length > 0 ? item.Title : item.ProjectId,
                item.Type,
                DependencyTypeDisplay(item.Type)))
            .ToList();
    }

    private async Task<string> GetDependencyProjectIdAsync(JsonElement dependency, CancellationToken cancellationToken)
    {
        if (dependency.TryGetProperty("project_id", out var projectId) && projectId.ValueKind == JsonValueKind.String)
            return projectId.GetString() ?? string.Empty;
        if (!dependency.TryGetProperty("version_id", out var versionId) || versionId.ValueKind != JsonValueKind.String)
            return string.Empty;

        using var document = await GetJsonAsync($"{_apiBase}/version/{Uri.EscapeDataString(versionId.GetString()!)}", cancellationToken);
        return document.RootElement.TryGetProperty("project_id", out var resolved) ? resolved.GetString() ?? string.Empty : string.Empty;
    }

    private async Task<string> DownloadPrimaryFileAsync(
        JsonElement version,
        string destinationDirectory,
        string fallbackName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var files = version.GetProperty("files").EnumerateArray().ToList();
        if (files.Count == 0)
            throw new InvalidDataException("Modrinth не вернул файл для скачивания.");
        var file = files.FirstOrDefault(item => item.TryGetProperty("primary", out var primary) && primary.GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined)
            file = files[0];

        var fileName = SanitizeFileName(file.GetProperty("filename").GetString() ?? fallbackName);
        var downloadUrl = file.GetProperty("url").GetString() ?? throw new InvalidDataException("Modrinth не вернул ссылку на файл.");
        var destination = Path.Combine(destinationDirectory, fileName);
        await DownloadFileAsync(downloadUrl, destination, progress, cancellationToken);

        if (file.TryGetProperty("hashes", out var hashes) && hashes.TryGetProperty("sha512", out var sha512))
        {
            await using var checkStream = File.OpenRead(destination);
            var actual = Convert.ToHexString(await SHA512.HashDataAsync(checkStream, cancellationToken));
            if (!actual.Equals(sha512.GetString(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                throw new InvalidDataException("Контрольная сумма загруженного файла не совпала.");
            }
        }
        return destination;
    }

    private async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[131072];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    if (length > 0)
                        progress?.Report(total * 100d / length.Value);
                }
            }
            File.Move(temporary, destination, true);
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string DestinationDirectory(string projectType, GameInstance instance) => projectType switch
    {
        "resourcepack" => Path.Combine(instance.DirectoryPath, "resourcepacks"),
        "shader" => Path.Combine(instance.DirectoryPath, "shaderpacks"),
        "datapack" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "datapacks"),
        "modpack" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "modpacks"),
        _ => instance.ModsPath
    };

    private static string EffectiveLoader(string projectType, string loader) => projectType switch
    {
        "datapack" => "datapack",
        "resourcepack" => string.IsNullOrWhiteSpace(loader) ? "minecraft" : loader,
        _ => loader
    };

    private static int LoaderOrder(string loader) => loader switch
    {
        "fabric" => 0,
        "forge" => 1,
        "neoforge" => 2,
        "quilt" => 3,
        "minecraft" => 4,
        "iris" => 5,
        "optifine" => 6,
        _ => 100
    };

    private static string LoaderDisplayName(string loader) => loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        "minecraft" => "Minecraft",
        "datapack" => "Data Pack",
        "iris" => "Iris",
        "optifine" => "OptiFine",
        _ => loader
    };

    private static string DependencyTypeDisplay(string type) => type switch
    {
        "required" => "Обязательная",
        "optional" => "Необязательная",
        "incompatible" => "Несовместимая",
        "embedded" => "Встроенная",
        _ => type
    };

    private static string CategoryDisplayName(string category) => category switch
    {
        "adventure" => "Приключения",
        "cursed" => "Необычное",
        "decoration" => "Декорации",
        "economy" => "Экономика",
        "equipment" => "Снаряжение",
        "food" => "Еда",
        "game-mechanics" => "Игровые механики",
        "library" => "Библиотеки",
        "magic" => "Магия",
        "management" => "Управление",
        "minigame" => "Мини-игры",
        "mobs" => "Мобы",
        "optimization" => "Оптимизация",
        "social" => "Социальное",
        "storage" => "Хранилища",
        "technology" => "Технологии",
        "transportation" => "Транспорт",
        "utility" => "Инструменты",
        "worldgen" => "Генерация мира",
        _ => category.Replace('-', ' ')
    };

    private static string CleanMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "Описание отсутствует.";
        var text = Regex.Replace(markdown, @"!\[[^\]]*\]\([^\)]+\)", string.Empty);
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        text = Regex.Replace(text, @"(?m)^#{1,6}\s*", string.Empty);
        text = Regex.Replace(text, @"(?i)<br\s*/?>|</p>|</h[1-6]>", "\n");
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = Regex.Replace(text, @"[*_`~>]", string.Empty);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return fileName;
    }
}
