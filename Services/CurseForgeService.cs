using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

public sealed class CurseForgeService
{
    private const int MinecraftGameId = 432;
    private readonly HttpClient _httpClient;
    private readonly string _apiBase;
    private readonly string _apiKey;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public CurseForgeService(HttpClient httpClient, string? apiBase, string? apiKey)
    {
        _httpClient = httpClient;
        _apiBase = string.IsNullOrWhiteSpace(apiBase) ? "https://api.curseforge.com/v1" : apiBase.TrimEnd('/');
        _apiKey = apiKey?.Trim() ?? string.Empty;
    }

    public async Task<IReadOnlyList<ModSearchOption>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync($"{_apiBase}/games/{MinecraftGameId}/versions", cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var groups))
            return [];

        return groups.EnumerateArray()
            .SelectMany(group => group.TryGetProperty("versions", out var versions) ? versions.EnumerateArray() : [])
            .Select(version => version.ValueKind == JsonValueKind.String
                ? version.GetString() ?? string.Empty
                : version.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty)
            .Where(version => Regex.IsMatch(version, @"^\d+(?:\.\d+)+$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(version => new ModSearchOption(version, version))
            .ToList();
    }

    public Task<IReadOnlyList<ModSearchOption>> GetLoadersAsync(string projectType, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModSearchOption> result = projectType is "mod" or "modpack"
            ?
            [
                new("neoforge", "NeoForge"),
                new("forge", "Forge"),
                new("fabric", "Fabric"),
                new("quilt", "Quilt")
            ]
            : [];
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<ModCategoryOption>> GetCategoriesAsync(string projectType, CancellationToken cancellationToken = default)
    {
        var classId = ClassId(projectType);
        using var document = await GetJsonAsync($"{_apiBase}/categories?gameId={MinecraftGameId}&classId={classId}", cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data))
            return [];

        return data.EnumerateArray()
            .Where(item => !item.TryGetProperty("isClass", out var isClass) || !isClass.GetBoolean())
            .Select(item => new ModCategoryOption(
                item.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : string.Empty,
                item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty))
            .Where(item => item.Id.Length > 0 && item.DisplayName.Length > 0)
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<ModrinthSearchResponse> SearchAsync(ModrinthSearchRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var query = new List<string>
        {
            $"gameId={MinecraftGameId}",
            $"classId={ClassId(request.ProjectType)}",
            $"index={Math.Max(0, request.Offset)}",
            $"pageSize={Math.Clamp(request.Limit, 1, 50)}",
            $"sortField={SortField(request.Index)}",
            "sortOrder=desc"
        };
        if (!string.IsNullOrWhiteSpace(request.Query))
            query.Add($"searchFilter={Uri.EscapeDataString(request.Query)}");
        if (!string.IsNullOrWhiteSpace(request.GameVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(request.GameVersion)}");
        if (int.TryParse(request.Category, out var categoryId))
            query.Add($"categoryId={categoryId}");
        if (request.ProjectType is "mod" or "modpack" && LoaderType(request.Loader) is var loaderType && loaderType > 0)
            query.Add($"modLoaderType={loaderType}");

        using var document = await GetJsonAsync($"{_apiBase}/mods/search?{string.Join('&', query)}", cancellationToken);
        var root = document.RootElement;
        var items = root.TryGetProperty("data", out var data)
            ? data.EnumerateArray().Select(project => ParseSearchResult(project, request.ProjectType)).ToList()
            : [];
        var total = root.TryGetProperty("pagination", out var pagination) && pagination.TryGetProperty("totalCount", out var totalCount)
            ? (int)Math.Min(int.MaxValue, totalCount.GetInt64())
            : items.Count;
        return new ModrinthSearchResponse(items, total);
    }

    public async Task<ModProjectDetails> GetProjectDetailsAsync(
        ModSearchResult result,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        using var projectDocument = await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(result.ProjectId)}", cancellationToken);
        var project = projectDocument.RootElement.GetProperty("data");

        string description;
        try
        {
            using var descriptionDocument = await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(result.ProjectId)}/description", cancellationToken);
            description = descriptionDocument.RootElement.TryGetProperty("data", out var body) && body.ValueKind == JsonValueKind.String
                ? body.GetString() ?? string.Empty
                : "Описание отсутствует.";
        }
        catch (HttpRequestException)
        {
            description = result.Description;
        }

        using var filesDocument = await GetCompatibleFilesAsync(result.ProjectId, gameVersion, result.ProjectType is "mod" or "modpack" ? loader : string.Empty, cancellationToken);
        var file = SelectBestFile(filesDocument.RootElement);
        var dependencies = file.ValueKind == JsonValueKind.Object
            ? await ResolveDependenciesAsync(file, cancellationToken)
            : [];

        var gallery = project.TryGetProperty("screenshots", out var screenshots)
            ? screenshots.EnumerateArray().Select(item => new ModGalleryImage
            {
                Url = StringValue(item, "url", StringValue(item, "thumbnailUrl")),
                Title = StringValue(item, "title"),
                Description = StringValue(item, "description")
            }).Where(image => image.Url.Length > 0).ToList()
            : [];
        var authors = project.TryGetProperty("authors", out var authorItems)
            ? string.Join(", ", authorItems.EnumerateArray().Select(item => StringValue(item, "name")).Where(name => name.Length > 0))
            : result.Author;
        var links = project.TryGetProperty("links", out var linksElement) ? linksElement : default;

        return new ModProjectDetails
        {
            ProjectId = result.ProjectId,
            Slug = StringValue(project, "slug"),
            WebsiteUrl = links.ValueKind == JsonValueKind.Object ? StringValue(links, "websiteUrl", result.WebsiteUrl) : result.WebsiteUrl,
            Title = StringValue(project, "name", result.Title),
            Summary = StringValue(project, "summary", result.Description),
            BodyText = description,
            ProjectType = result.ProjectType,
            Author = authors,
            IconUrl = project.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object ? StringValue(logo, "url", StringValue(logo, "thumbnailUrl", result.IconUrl)) : result.IconUrl,
            Downloads = LongValue(project, "downloadCount", result.Downloads),
            UpdatedAt = DateValue(project, "dateModified") ?? result.DateModified,
            License = "Не указана",
            SourceUrl = links.ValueKind == JsonValueKind.Object ? StringValue(links, "sourceUrl") : string.Empty,
            IssuesUrl = links.ValueKind == JsonValueKind.Object ? StringValue(links, "issuesUrl") : string.Empty,
            VersionName = file.ValueKind == JsonValueKind.Object ? StringValue(file, "displayName", StringValue(file, "fileName")) : string.Empty,
            GameVersions = ProjectGameVersions(project),
            Loaders = ProjectLoaders(project),
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
        CancellationToken cancellationToken = default,
        int? selectedFileId = null)
    {
        EnsureConfigured();
        var gameVersion = project.ProjectType == "modpack" && selectedGameVersion.Length > 0 ? selectedGameVersion : instance.GameVersion;
        var loader = project.ProjectType switch
        {
            "mod" => string.Equals(instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase) ? selectedLoader : instance.Loader.ToLowerInvariant(),
            "modpack" => selectedLoader,
            _ => string.Empty
        };
        if (project.ProjectType == "mod" && LoaderType(loader) == 0)
            throw new InvalidOperationException("Выберите NeoForge, Forge, Fabric или Quilt.");

        if (project.ProjectType == "mod")
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mode = dependencyDownloadMode is "all" or "none" ? dependencyDownloadMode : "required";
            var path = await InstallModProjectAsync(project.ProjectId, instance, loader, mode, visited, installed, progress, cancellationToken, selectedFileId);
            var dependencyCount = Math.Max(0, installed.Count - 1);
            var dependencyText = dependencyCount > 0 ? $" Зависимостей установлено: {dependencyCount}." : string.Empty;
            return new ModDownloadResult(path, $"Мод установлен в сборку «{instance.Name}».{dependencyText}", true);
        }

        using var filesDocument = selectedFileId is null
            ? await GetCompatibleFilesAsync(project.ProjectId, gameVersion, loader, cancellationToken)
            : await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(project.ProjectId)}/files/{selectedFileId}", cancellationToken);
        var file = selectedFileId is null ? SelectBestFile(filesDocument.RootElement) : filesDocument.RootElement.GetProperty("data");
        if (file.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Для выбранной версии Minecraft нет совместимого файла.");
        var destination = DestinationDirectory(project.ProjectType, instance);
        Directory.CreateDirectory(destination);
        var installedPath = await DownloadFileEntryAsync(project.ProjectId, file, destination, progress, cancellationToken);
        return project.ProjectType switch
        {
            "resourcepack" => new ModDownloadResult(installedPath, $"Ресурспак установлен в сборку «{instance.Name}».", true),
            "shader" => new ModDownloadResult(installedPath, $"Шейдер установлен в сборку «{instance.Name}».", true),
            "datapack" => new ModDownloadResult(installedPath, "Датапак скачан. При следующем запуске игры он появится в меню датапаков при создании мира.", false),
            "modpack" => new ModDownloadResult(installedPath, "Модпак скачан в папку загрузок сборки.", false),
            _ => new ModDownloadResult(installedPath, "Файл скачан.", false)
        };
    }

    private async Task<string> InstallModProjectAsync(
        string projectId,
        GameInstance instance,
        string loader,
        string dependencyDownloadMode,
        HashSet<string> visited,
        HashSet<string> installed,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        int? selectedFileId = null)
    {
        if (!visited.Add(projectId))
            return string.Empty;
        var completed = false;
        try
        {
            using var filesDocument = selectedFileId is null
                ? await GetCompatibleFilesAsync(projectId, instance.GameVersion, loader, cancellationToken)
                : await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(projectId)}/files/{selectedFileId}", cancellationToken);
            var file = selectedFileId is null ? SelectBestFile(filesDocument.RootElement) : filesDocument.RootElement.GetProperty("data");
            if (file.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("У мода или одной из его зависимостей нет совместимого файла для этой сборки.");

            if (dependencyDownloadMode != "none" && file.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateArray())
                {
                    var relation = dependency.TryGetProperty("relationType", out var relationType) ? relationType.GetInt32() : 0;
                    var shouldInstall = relation == 3 || dependencyDownloadMode == "all" && relation == 2;
                    if (!shouldInstall || !dependency.TryGetProperty("modId", out var dependencyId))
                        continue;
                    try
                    {
                        await InstallModProjectAsync(dependencyId.GetInt32().ToString(), instance, loader, dependencyDownloadMode, visited, installed, null, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && relation == 2)
                    {
                        // Необязательная зависимость не блокирует установку основного мода.
                    }
                }
            }

            Directory.CreateDirectory(instance.ModsPath);
            var path = await DownloadFileEntryAsync(projectId, file, instance.ModsPath, progress, cancellationToken);
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

    private async Task<JsonDocument> GetCompatibleFilesAsync(string projectId, string gameVersion, string loader, CancellationToken cancellationToken, int index = 0)
    {
        var query = new List<string> { "pageSize=50", $"index={index}" };
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        if (LoaderType(loader) is var loaderType && loaderType > 0)
            query.Add($"modLoaderType={loaderType}");
        return await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(projectId)}/files?{string.Join('&', query)}", cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogVersion>> GetProjectVersionsAsync(
        string projectId, string gameVersion, string loader, CancellationToken cancellationToken = default)
    {
        var result = new List<CatalogVersion>();
        for (var index = 0; index < 300; index += 50)
        {
            using var document = await GetCompatibleFilesAsync(projectId, gameVersion, loader, cancellationToken, index);
            if (!document.RootElement.TryGetProperty("data", out var files) || files.GetArrayLength() == 0)
                break;
            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("isAvailable", out var available) && !available.GetBoolean())
                    continue;
                var gameVersions = file.TryGetProperty("gameVersions", out var values)
                    ? values.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray() : [];
                var loaders = gameVersions.Where(value => LoaderType(value) > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                result.Add(new CatalogVersion(
                    file.GetProperty("id").GetInt32().ToString(),
                    StringValue(file, "displayName", StringValue(file, "fileName")),
                    StringValue(file, "fileName"),
                    file.TryGetProperty("releaseType", out var release) ? release.GetInt32() switch { 1 => "release", 2 => "beta", 3 => "alpha", _ => "release" } : "release",
                    DateValue(file, "fileDate"),
                    gameVersions.Where(value => Regex.IsMatch(value, @"^\d+(?:\.\d+)+$")).ToArray(),
                    loaders));
            }
            if (files.GetArrayLength() < 50)
                break;
        }
        return result.OrderByDescending(item => item.PublishedAt).ToArray();
    }

    /// <summary>Downloads one catalog file into a caller-owned staging directory for verified replacement.</summary>
    public async Task<string> DownloadFileForRepairAsync(string projectId, int fileId, string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(projectId)}/files/{fileId}", cancellationToken);
        var file = document.RootElement.GetProperty("data");
        if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("id", out var id) || id.GetInt32() != fileId)
            throw new InvalidDataException("CurseForge вернул другой файл.");
        Directory.CreateDirectory(stagingDirectory);
        return await DownloadFileEntryAsync(projectId, file, stagingDirectory, null, cancellationToken);
    }

    private async Task<IReadOnlyList<ModDependencyInfo>> ResolveDependenciesAsync(JsonElement file, CancellationToken cancellationToken)
    {
        if (!file.TryGetProperty("dependencies", out var dependencies))
            return [];
        var raw = dependencies.EnumerateArray()
            .Where(item => item.TryGetProperty("modId", out _))
            .Select(item => (Id: item.GetProperty("modId").GetInt32(), Relation: item.TryGetProperty("relationType", out var relation) ? relation.GetInt32() : 0))
            .Where(item => item.Relation is 2 or 3 or 5)
            .ToList();
        if (raw.Count == 0)
            return [];

        var names = new Dictionary<int, string>();
        try
        {
            using var projectsDocument = await PostJsonAsync($"{_apiBase}/mods", new { modIds = raw.Select(item => item.Id).Distinct().ToArray() }, cancellationToken);
            if (projectsDocument.RootElement.TryGetProperty("data", out var projects))
                foreach (var project in projects.EnumerateArray())
                    if (project.TryGetProperty("id", out var id))
                        names[id.GetInt32()] = StringValue(project, "name", id.GetInt32().ToString());
        }
        catch (HttpRequestException)
        {
            foreach (var dependency in raw)
                names.TryAdd(dependency.Id, dependency.Id.ToString());
        }

        return raw.Select(item => new ModDependencyInfo(
            item.Id.ToString(),
            names.TryGetValue(item.Id, out var name) ? name : item.Id.ToString(),
            item.Relation == 3 ? "required" : item.Relation == 2 ? "optional" : "incompatible",
            item.Relation == 3 ? "Обязательная" : item.Relation == 2 ? "Необязательная" : "Несовместимая")).ToList();
    }

    private async Task<string> DownloadFileEntryAsync(string projectId, JsonElement file, string destinationDirectory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var fileId = file.GetProperty("id").GetInt32();
        var downloadUrl = StringValue(file, "downloadUrl");
        if (downloadUrl.Length == 0)
        {
            try
            {
                using var urlDocument = await GetJsonAsync($"{_apiBase}/mods/{Uri.EscapeDataString(projectId)}/files/{fileId}/download-url", cancellationToken);
                downloadUrl = urlDocument.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : string.Empty;
            }
            catch (HttpRequestException)
            {
                // Некоторые авторы отключают распространение файлов через сторонние клиенты.
            }
        }
        if (downloadUrl.Length == 0)
            throw new InvalidOperationException("Автор проекта запретил скачивание файла через сторонние приложения. Откройте страницу проекта на CurseForge.");

        var fileName = SanitizeFileName(StringValue(file, "fileName", $"{projectId}-{fileId}"));
        var destination = Path.Combine(destinationDirectory, fileName);
        var temporary = destination + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
            await ValidateHashAsync(temporary, file, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }
    }

    private static async Task ValidateHashAsync(string path, JsonElement file, CancellationToken cancellationToken)
    {
        if (!file.TryGetProperty("hashes", out var hashes))
            return;
        var hash = hashes.EnumerateArray().FirstOrDefault(item => item.TryGetProperty("algo", out var algo) && algo.GetInt32() == 1);
        if (hash.ValueKind == JsonValueKind.Undefined)
            return;
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));
        if (!actual.Equals(StringValue(hash, "value"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Контрольная сумма загруженного файла CurseForge не совпала.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("x-api-key", _apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Ключ CurseForge API отсутствует, недействителен или не имеет доступа. Проверьте конфигурацию сборки.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("x-api-key", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Ключ CurseForge API недействителен или не имеет доступа.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Для CurseForge нужен API key, включённый автором в конфигурацию сборки.");
    }

    private static ModSearchResult ParseSearchResult(JsonElement project, string projectType)
    {
        var links = project.TryGetProperty("links", out var linksElement) ? linksElement : default;
        var logo = project.TryGetProperty("logo", out var logoElement) ? logoElement : default;
        var author = project.TryGetProperty("authors", out var authors)
            ? authors.EnumerateArray().Select(item => StringValue(item, "name")).FirstOrDefault(name => name.Length > 0) ?? string.Empty
            : string.Empty;
        return new ModSearchResult
        {
            ProjectId = project.GetProperty("id").GetInt32().ToString(),
            SourceId = "curseforge",
            WebsiteUrl = links.ValueKind == JsonValueKind.Object ? StringValue(links, "websiteUrl") : string.Empty,
            Title = StringValue(project, "name", "Без названия"),
            Description = StringValue(project, "summary"),
            Author = author,
            IconUrl = logo.ValueKind == JsonValueKind.Object ? StringValue(logo, "url", StringValue(logo, "thumbnailUrl")) : string.Empty,
            ProjectType = projectType,
            DateModified = DateValue(project, "dateModified"),
            Downloads = LongValue(project, "downloadCount"),
            Categories = project.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array
                ? categories.EnumerateArray().Select(item => StringValue(item, "name")).Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList()
                : [],
            Loaders = projectType is "mod" or "modpack" ? ProjectLoaders(project) : []
        };
    }

    private static JsonElement SelectBestFile(JsonElement responseRoot)
    {
        if (!responseRoot.TryGetProperty("data", out var files) || files.GetArrayLength() == 0)
            return default;
        return files.EnumerateArray()
            .Where(file => !file.TryGetProperty("isAvailable", out var available) || available.GetBoolean())
            .OrderBy(file => file.TryGetProperty("releaseType", out var releaseType) ? releaseType.GetInt32() : 9)
            .ThenByDescending(file => DateValue(file, "fileDate") ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> ProjectGameVersions(JsonElement project) => project.TryGetProperty("latestFilesIndexes", out var indexes)
        ? indexes.EnumerateArray().Select(item => StringValue(item, "gameVersion")).Where(value => value.Length > 0).Distinct().ToList()
        : [];

    private static IReadOnlyList<string> ProjectLoaders(JsonElement project) => project.TryGetProperty("latestFilesIndexes", out var indexes)
        ? indexes.EnumerateArray().Select(item => item.TryGetProperty("modLoader", out var loader) ? LoaderDisplay(loader.GetInt32()) : string.Empty).Where(value => value.Length > 0).Distinct().ToList()
        : [];

    private static int ClassId(string projectType) => projectType switch
    {
        "modpack" => 4471,
        "resourcepack" => 12,
        "shader" => 6552,
        "datapack" => 6945,
        _ => 6
    };

    private static int LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0
    };

    private static string LoaderDisplay(int loader) => loader switch
    {
        1 => "Forge",
        4 => "Fabric",
        5 => "Quilt",
        6 => "NeoForge",
        _ => string.Empty
    };

    private static int SortField(string sort) => sort switch
    {
        "downloads" => 6,
        "updated" => 3,
        "newest" => 11,
        "follows" => 12,
        _ => 2
    };

    private static string DestinationDirectory(string projectType, GameInstance instance) => projectType switch
    {
        "resourcepack" => Path.Combine(instance.DirectoryPath, "resourcepacks"),
        "shader" => Path.Combine(instance.DirectoryPath, "shaderpacks"),
        "datapack" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "datapacks"),
        "modpack" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "modpacks"),
        _ => instance.ModsPath
    };

    private static string StringValue(JsonElement element, string property, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static long LongValue(JsonElement element, string property, long fallback = 0) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : fallback;

    private static DateTime? DateValue(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTime(out var result) ? result : null;

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return fileName;
    }
}
