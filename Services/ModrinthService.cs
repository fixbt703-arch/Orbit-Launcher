using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

public sealed class ModrinthService
{
	private readonly HttpClient _httpClient;

	private readonly string _apiBase;

	public ModrinthService(HttpClient httpClient, string? apiBase = null)
	{
		_httpClient = httpClient;
		_apiBase = (string.IsNullOrWhiteSpace(apiBase) ? "https://api.modrinth.com/v2" : apiBase.TrimEnd('/'));
	}

	public async Task<IReadOnlyDictionary<string, string>> GetProjectIdsByHashesAsync(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<string> list = hashes.Where((string hash) => !string.IsNullOrWhiteSpace(hash)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (list.Count == 0)
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}
		Dictionary<string, string> projectsByHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string[] item in list.Chunk(100))
		{
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_apiBase + "/version_files", new
			{
				hashes = item,
				algorithm = "sha1"
			}, cancellationToken);
			response.EnsureSuccessStatusCode();
			await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
			foreach (JsonProperty item2 in jsonDocument.RootElement.EnumerateObject())
			{
				if (item2.Value.TryGetProperty("project_id", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
				{
					projectsByHash[item2.Name] = value.GetString() ?? throw new InvalidDataException("Modrinth вернул пустой ID проекта.");
				}
			}
		}
		return projectsByHash;
	}

	public async Task<IReadOnlyList<ModSearchOption>> GetGameVersionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using JsonDocument jsonDocument = await GetJsonAsync(_apiBase + "/tag/game_version", cancellationToken);
		return (from item in jsonDocument.RootElement.EnumerateArray()
			where item.GetProperty("version_type").GetString() == "release"
			select item.GetProperty("version").GetString() ?? string.Empty into version
			where !string.IsNullOrWhiteSpace(version)
			select new ModSearchOption(version, version)).ToList();
	}

	public async Task<IReadOnlyList<ModSearchOption>> GetLoadersAsync(string projectType, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (projectType == "datapack")
		{
			return new global::_003C_003Ez__ReadOnlySingleElementList<ModSearchOption>(new ModSearchOption("datapack", "Data Pack"));
		}
		using JsonDocument jsonDocument = await GetJsonAsync(_apiBase + "/tag/loader", cancellationToken);
		JsonElement value;
		return (from name in (from item in jsonDocument.RootElement.EnumerateArray()
				where item.TryGetProperty("supported_project_types", out value) && value.EnumerateArray().Any((JsonElement type) => type.GetString() == projectType)
				select item.GetProperty("name").GetString() ?? string.Empty into name
				where !string.IsNullOrWhiteSpace(name)
				select name).Where(delegate(string name)
			{
				bool flag = projectType != "mod";
				if (!flag)
				{
					bool flag2;
					switch (name)
					{
					case "neoforge":
					case "forge":
					case "fabric":
					case "quilt":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = flag2;
				}
				return flag;
			}).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy(LoaderOrder)
				.ThenBy((string name) => name)
			select new ModSearchOption(name, LoaderDisplayName(name))).ToList();
	}

	public async Task<IReadOnlyList<ModCategoryOption>> GetCategoriesAsync(string projectType, CancellationToken cancellationToken = default(CancellationToken))
	{
		string apiProjectType = ((projectType == "datapack") ? "mod" : projectType);
		using JsonDocument jsonDocument = await GetJsonAsync(_apiBase + "/tag/category", cancellationToken);
		JsonElement value;
		return (from name in (from item in jsonDocument.RootElement.EnumerateArray()
				where item.TryGetProperty("project_type", out value) && value.GetString() == apiProjectType
				where item.TryGetProperty("header", out value) && value.GetString() == "categories"
				select item.GetProperty("name").GetString() ?? string.Empty into name
				where !string.IsNullOrWhiteSpace(name) && name != "datapack"
				select name).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy(CategoryDisplayName)
			select new ModCategoryOption(name, CategoryDisplayName(name))).ToList();
	}

	public async Task<ModrinthSearchResponse> SearchAsync(ModrinthSearchRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		string text = ((request.ProjectType == "datapack") ? "mod" : request.ProjectType);
		List<string[]> list = new List<string[]>();
		list.Add(new string[1] { "project_type:" + text });
		List<string[]> list2 = list;
		if (request.ProjectType == "datapack")
		{
			list2.Add(new string[1] { "categories:datapack" });
		}
		if (!string.IsNullOrWhiteSpace(request.GameVersion))
		{
			list2.Add(new string[1] { "versions:" + request.GameVersion });
		}
		if (!string.IsNullOrWhiteSpace(request.Loader))
		{
			list2.Add(new string[1] { "categories:" + request.Loader });
		}
		if (!string.IsNullOrWhiteSpace(request.Category))
		{
			list2.Add(new string[1] { "categories:" + request.Category });
		}
		if (request.Environment == "client")
		{
			list2.Add(new string[1] { "client_side!=unsupported" });
		}
		else if (request.Environment == "server")
		{
			list2.Add(new string[1] { "server_side!=unsupported" });
		}
		bool flag;
		switch (request.Index)
		{
		case "downloads":
		case "follows":
		case "newest":
		case "updated":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		string value = (flag ? request.Index : "relevance");
		int value2 = Math.Clamp(request.Limit, 1, 100);
		int value3 = Math.Max(0, request.Offset);
		string url = $"{_apiBase}/search?query={Uri.EscapeDataString(request.Query ?? string.Empty)}&limit={value2}&offset={value3}&index={value}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(list2))}";
		using JsonDocument jsonDocument = await GetJsonAsync(url, cancellationToken);
		JsonElement value5;
		JsonElement value6;
		JsonElement value7;
		JsonElement value8;
		JsonElement value9;
		JsonElement value10;
		JsonElement value11;
		long value12;
		List<ModSearchResult> list3 = (from hit in jsonDocument.RootElement.GetProperty("hits").EnumerateArray()
			select new ModSearchResult
			{
				ProjectId = (hit.GetProperty("project_id").GetString() ?? string.Empty),
				SourceId = "modrinth",
				Title = (hit.GetProperty("title").GetString() ?? "Без названия"),
				Description = (hit.GetProperty("description").GetString() ?? string.Empty),
				Author = (hit.TryGetProperty("author", out value5) ? (value5.GetString() ?? string.Empty) : string.Empty),
				IconUrl = ((hit.TryGetProperty("icon_url", out value6) && value6.ValueKind == JsonValueKind.String) ? (value6.GetString() ?? string.Empty) : string.Empty),
				ProjectType = ((request.ProjectType == "datapack") ? "datapack" : (hit.TryGetProperty("project_type", out value7) ? (value7.GetString() ?? request.ProjectType) : request.ProjectType)),
				LatestVersion = ((hit.TryGetProperty("latest_version", out value8) && value8.ValueKind == JsonValueKind.String) ? (value8.GetString() ?? string.Empty) : string.Empty),
				DateModified = ((hit.TryGetProperty("date_modified", out value9) && value9.ValueKind == JsonValueKind.String) ? new DateTime?(value9.GetDateTime()) : ((DateTime?)null)),
				Downloads = (hit.TryGetProperty("downloads", out value10) ? value10.GetInt64() : 0),
				Followers = ((hit.TryGetProperty("follows", out value11) && value11.TryGetInt64(out value12)) ? new long?(value12) : ((long?)null)),
				Categories = SearchCategories(hit, request.ProjectType),
				Loaders = SearchLoaders(hit),
				Environment = SearchEnvironment(hit)
			}).ToList();
		JsonElement value4;
		int totalHits = (jsonDocument.RootElement.TryGetProperty("total_hits", out value4) ? value4.GetInt32() : list3.Count);
		return new ModrinthSearchResponse(list3, totalHits);
	}

	private static IReadOnlyList<string> SearchCategories(JsonElement hit, string projectType)
	{
		JsonElement value;
		JsonElement value2;
		JsonElement jsonElement = ((hit.TryGetProperty("display_categories", out value) && value.ValueKind == JsonValueKind.Array) ? value : ((hit.TryGetProperty("categories", out value2) && value2.ValueKind == JsonValueKind.Array) ? value2 : default(JsonElement)));
		if (jsonElement.ValueKind == JsonValueKind.Array)
		{
			return (from item in jsonElement.EnumerateArray()
				where item.ValueKind == JsonValueKind.String
				select item.GetString() ?? string.Empty).Where(delegate(string item)
			{
				bool flag = item.Length > 0;
				if (flag)
				{
					string text = projectType;
					bool flag2;
					if ((text == "mod" || text == "modpack") ? true : false)
					{
						flag2 = IsModCategory(item);
					}
					else
					{
						bool flag3;
						switch (item)
						{
						case "fabric":
						case "forge":
						case "neoforge":
						case "quilt":
							flag3 = true;
							break;
						default:
							flag3 = false;
							break;
						}
						flag2 = !flag3;
					}
					flag = flag2;
				}
				return flag;
			}).Distinct<string>(StringComparer.OrdinalIgnoreCase).Take(3)
				.Select(CategoryDisplayName)
				.ToList();
		}
		return new List<string>();
	}

	private static bool IsModCategory(string category)
	{
		switch (category)
		{
		case "adventure":
		case "equipment":
		case "cursed":
		case "social":
		case "decoration":
		case "management":
		case "technology":
		case "storage":
		case "utility":
		case "economy":
		case "library":
		case "food":
		case "mobs":
		case "game-mechanics":
		case "transportation":
		case "minigame":
		case "worldgen":
		case "magic":
		case "optimization":
			return true;
		default:
			return false;
		}
	}

	private static IReadOnlyList<string> SearchLoaders(JsonElement hit)
	{
		if (!hit.TryGetProperty("categories", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return new List<string>();
		}
		return (from item in value.EnumerateArray()
			where item.ValueKind == JsonValueKind.String
			select item.GetString() ?? string.Empty).Where(delegate(string item)
		{
			switch (item)
			{
			case "fabric":
			case "forge":
			case "neoforge":
			case "quilt":
				return true;
			default:
				return false;
			}
		}).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy(LoaderOrder)
			.Select(LoaderDisplayName)
			.ToList();
	}

	private static string SearchEnvironment(JsonElement hit)
	{
		JsonElement value;
		bool flag = hit.TryGetProperty("client_side", out value) && value.ValueKind == JsonValueKind.String && value.GetString() != "unsupported";
		JsonElement value2;
		bool flag2 = hit.TryGetProperty("server_side", out value2) && value2.ValueKind == JsonValueKind.String && value2.GetString() != "unsupported";
		if (!(flag && flag2))
		{
			if (!flag)
			{
				if (!flag2)
				{
					return string.Empty;
				}
				return "Сервер";
			}
			return "Клиент";
		}
		return "Клиент и сервер";
	}

	public async Task<ModProjectDetails> GetProjectDetailsAsync(ModSearchResult result, string gameVersion, string loader, CancellationToken cancellationToken = default(CancellationToken))
	{
		using JsonDocument projectDocument = await GetJsonAsync(_apiBase + "/project/" + Uri.EscapeDataString(result.ProjectId), cancellationToken);
		JsonElement project = projectDocument.RootElement;
		using JsonDocument versionsDocument = await GetVersionsAsync(result.ProjectId, gameVersion, EffectiveLoader(result.ProjectType, loader), cancellationToken);
		JsonElement version = ((versionsDocument.RootElement.GetArrayLength() > 0) ? versionsDocument.RootElement[0] : default(JsonElement));
		IReadOnlyList<ModDependencyInfo> readOnlyList = ((version.ValueKind != JsonValueKind.Object) ? Array.Empty<ModDependencyInfo>() : (await ResolveDependenciesAsync(version, cancellationToken)));
		IReadOnlyList<ModDependencyInfo> dependencies = readOnlyList;
		JsonElement value;
		JsonElement value18;
		JsonElement value19;
		List<ModGalleryImage> gallery = (project.TryGetProperty("gallery", out value) ? (from item in value.EnumerateArray()
			orderby item.TryGetProperty("featured", out value18) && value18.GetBoolean() descending, item.TryGetProperty("ordering", out value18) ? value18.GetInt32() : 0
			select new ModGalleryImage
			{
				Url = (item.GetProperty("url").GetString() ?? string.Empty),
				Title = ((item.TryGetProperty("title", out value18) && value18.ValueKind == JsonValueKind.String) ? (value18.GetString() ?? string.Empty) : string.Empty),
				Description = ((item.TryGetProperty("description", out value19) && value19.ValueKind == JsonValueKind.String) ? (value19.GetString() ?? string.Empty) : string.Empty)
			} into image
			where !string.IsNullOrWhiteSpace(image.Url)
			select image).ToList() : new List<ModGalleryImage>());
		JsonElement value2;
		JsonElement value3;
		JsonElement value4;
		JsonElement value5;
		JsonElement value6;
		JsonElement value7;
		JsonElement value8;
		JsonElement value9;
		JsonElement value10;
		JsonElement value11;
		JsonElement value12;
		JsonElement value13;
		JsonElement value14;
		JsonElement value15;
		JsonElement value16;
		JsonElement value17;
		return new ModProjectDetails
		{
			ProjectId = result.ProjectId,
			Slug = (project.TryGetProperty("slug", out value2) ? (value2.GetString() ?? string.Empty) : string.Empty),
			WebsiteUrl = "https://modrinth.com/project/" + (project.TryGetProperty("slug", out value3) ? (value3.GetString() ?? result.ProjectId) : result.ProjectId),
			Title = (project.TryGetProperty("title", out value4) ? (value4.GetString() ?? result.Title) : result.Title),
			Summary = (project.TryGetProperty("description", out value5) ? (value5.GetString() ?? result.Description) : result.Description),
			BodyText = (project.TryGetProperty("body", out value6) ? (value6.GetString() ?? string.Empty) : string.Empty),
			ProjectType = result.ProjectType,
			Author = result.Author,
			IconUrl = ((project.TryGetProperty("icon_url", out value7) && value7.ValueKind == JsonValueKind.String) ? (value7.GetString() ?? result.IconUrl) : result.IconUrl),
			Downloads = (project.TryGetProperty("downloads", out value8) ? value8.GetInt64() : result.Downloads),
			Followers = (project.TryGetProperty("followers", out value9) ? value9.GetInt64() : 0),
			UpdatedAt = ((project.TryGetProperty("updated", out value10) && value10.ValueKind == JsonValueKind.String) ? new DateTime?(value10.GetDateTime()) : result.DateModified),
			License = ((project.TryGetProperty("license", out value11) && value11.TryGetProperty("name", out value12)) ? (value12.GetString() ?? "Не указана") : "Не указана"),
			SourceUrl = ((project.TryGetProperty("source_url", out value13) && value13.ValueKind == JsonValueKind.String) ? (value13.GetString() ?? string.Empty) : string.Empty),
			IssuesUrl = ((project.TryGetProperty("issues_url", out value14) && value14.ValueKind == JsonValueKind.String) ? (value14.GetString() ?? string.Empty) : string.Empty),
			VersionName = ((version.ValueKind == JsonValueKind.Object && version.TryGetProperty("version_number", out value15)) ? (value15.GetString() ?? string.Empty) : string.Empty),
			GameVersions = (project.TryGetProperty("game_versions", out value16) ? (from item in value16.EnumerateArray()
				select item.GetString() ?? string.Empty into text
				where text.Length > 0
				select text).ToList() : new List<string>()),
			Loaders = (project.TryGetProperty("loaders", out value17) ? (from item in value17.EnumerateArray()
				select item.GetString() ?? string.Empty into text
				where text.Length > 0
				select text).ToList() : new List<string>()),
			Dependencies = dependencies,
			Gallery = gallery,
			SearchResult = result
		};
	}

	public async Task<ModDownloadResult> DownloadAsync(ModSearchResult project, GameInstance instance, string selectedGameVersion, string selectedLoader, string dependencyDownloadMode, IProgress<double>? progress = null, CancellationToken cancellationToken = default(CancellationToken), string? selectedVersionId = null)
	{
		string gameVersion = ((!(project.ProjectType == "modpack")) ? instance.GameVersion : ((selectedGameVersion.Length > 0) ? selectedGameVersion : instance.GameVersion));
		string text = project.ProjectType switch
		{
			"mod" => string.Equals(instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase) ? selectedLoader : instance.Loader.ToLowerInvariant(), 
			"datapack" => "datapack", 
			"resourcepack" => string.IsNullOrWhiteSpace(selectedLoader) ? "minecraft" : selectedLoader, 
			"shader" => selectedLoader, 
			"modpack" => selectedLoader, 
			_ => selectedLoader, 
		};
		if (project.ProjectType == "mod" && string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("Выберите загрузчик модов или используйте сборку Fabric, Forge, NeoForge либо Quilt.");
		}
		string destinationDirectory = DestinationDirectory(project.ProjectType, instance);
		Directory.CreateDirectory(destinationDirectory);
		if (project.ProjectType == "mod")
		{
			HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool flag = ((dependencyDownloadMode == "all" || dependencyDownloadMode == "none") ? true : false);
			string mode = (flag ? dependencyDownloadMode : "required");
			string destinationPath = await InstallModProjectAsync(project.ProjectId, instance, text, mode, installed, progress, cancellationToken, selectedVersionId);
			int num = Math.Max(0, installed.Count - 1);
			return new ModDownloadResult(destinationPath, string.Concat(str3: (num == 0) ? string.Empty : ((mode == "all") ? $" Зависимостей установлено: {num}." : $" Обязательных зависимостей установлено: {num}."), str0: "Мод установлен в сборку «", str1: instance.Name, str2: "»."), InstalledDirectly: true);
		}
		JsonDocument jsonDocument = ((selectedVersionId != null) ? (await GetJsonAsync(_apiBase + "/version/" + Uri.EscapeDataString(selectedVersionId), cancellationToken)) : (await GetVersionsAsync(project.ProjectId, gameVersion, EffectiveLoader(project.ProjectType, text), cancellationToken)));
		using JsonDocument versionsDocument = jsonDocument;
		if (selectedVersionId == null && versionsDocument.RootElement.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("Для выбранной версии Minecraft и загрузчика нет совместимого файла.");
		}
		JsonElement version = ((selectedVersionId == null) ? versionsDocument.RootElement[0] : versionsDocument.RootElement);
		string text2 = await DownloadPrimaryFileAsync(version, destinationDirectory, project.ProjectId, progress, cancellationToken);
		if (project.ProjectType == "modpack")
		{
			return await new MrpackInstaller().InstallAsync(text2, instance, progress, cancellationToken);
		}
		return project.ProjectType switch
		{
			"resourcepack" => new ModDownloadResult(text2, "Ресурспак установлен в сборку «" + instance.Name + "».", InstalledDirectly: true), 
			"shader" => new ModDownloadResult(text2, "Шейдер установлен в сборку «" + instance.Name + "».", InstalledDirectly: true), 
			"datapack" => new ModDownloadResult(text2, "Датапак скачан. При следующем запуске игры он появится в меню датапаков при создании мира.", InstalledDirectly: false), 
			_ => new ModDownloadResult(text2, "Файл скачан.", InstalledDirectly: false), 
		};
	}

	private async Task<string> InstallModProjectAsync(string projectId, GameInstance instance, string loader, string dependencyDownloadMode, HashSet<string> installed, IProgress<double>? progress, CancellationToken cancellationToken, string? selectedVersionId = null)
    {
        var plan = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var pinned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var optionalBranches = new List<(string Origin, string Version, JsonElement Dependency)>();
        var mandatoryProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Resolve(string id, string? versionId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (versionId is not null && mandatoryProjects.Contains(id) && plan[id].GetProperty("id").GetString() != versionId)
                throw new InvalidDataException("Необязательный мод требует другую версию обязательной зависимости: " + id);
            if (mandatoryProjects.Count > 0 && versionId is not null && pinned.TryGetValue(id, out var required) && required != versionId)
                throw new InvalidDataException("Моды требуют разные версии одной зависимости: " + id);
            if (versionId is not null) pinned[id] = versionId;
            if (plan.TryGetValue(id, out var previous) && (versionId is null || previous.GetProperty("id").GetString() == versionId)) return;
            if (!resolving.Add(id))
            {
                if (versionId is not null && plan.TryGetValue(id, out previous) && previous.GetProperty("id").GetString() != versionId)
                    throw new InvalidDataException("Обнаружен конфликт циклических зависимостей: " + id);
                return;
            }
            try
            {
                using var document = versionId is null ? await GetVersionsAsync(id, instance.GameVersion, loader, cancellationToken)
                    : await GetJsonAsync(_apiBase + "/version/" + Uri.EscapeDataString(versionId), cancellationToken);
                if (versionId is null && document.RootElement.GetArrayLength() == 0)
                    throw new InvalidOperationException("У мода или зависимости нет совместимой версии: " + id);
                var version = (versionId is null ? document.RootElement[0] : document.RootElement).Clone();
                if (version.TryGetProperty("project_id", out var owner) && owner.GetString() != id)
                    throw new InvalidDataException("Версия зависимости принадлежит другому проекту: " + id);
                if ((version.TryGetProperty("game_versions", out var games) && !games.EnumerateArray().Any(g => g.GetString() == instance.GameVersion))
                    || (version.TryGetProperty("loaders", out var loaders) && !loaders.EnumerateArray().Any(l => string.Equals(l.GetString(), loader, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidDataException("Требуемый выпуск мода несовместим со сборкой: " + id);
                plan[id] = version;
                if (plan.Count > 512) throw new InvalidDataException("Слишком большое дерево зависимостей модов.");
                if (dependencyDownloadMode == "none" || !version.TryGetProperty("dependencies", out var dependencies)) return;
                foreach (var dependency in dependencies.EnumerateArray())
                {
                    var type = dependency.TryGetProperty("dependency_type", out var kind) ? kind.GetString() : "";
                    if (type == "optional" && dependencyDownloadMode == "all")
                    {
                        optionalBranches.Add((id, version.GetProperty("id").GetString() ?? "", dependency.Clone()));
                        continue;
                    }
                    if (type != "required") continue;
                    var dependencyId = await GetDependencyProjectIdAsync(dependency, cancellationToken);
                    if (dependencyId.Length == 0) throw new InvalidDataException("Modrinth не вернул ID зависимости проекта " + id);
                    var pin = dependency.TryGetProperty("version_id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    await Resolve(dependencyId, pin);
                }
            }
            finally { resolving.Remove(id); }
        }
        await Resolve(projectId, selectedVersionId);
        // A later dependency pin can replace a provisional latest release and its constraints.
        pinned.Clear();
        if (selectedVersionId is not null) pinned[projectId] = selectedVersionId;
        void ValidateRequired(string id)
        {
            if (!mandatoryProjects.Add(id)) return;
            var version = plan[id];
            if (dependencyDownloadMode == "none" || !version.TryGetProperty("dependencies", out var dependencies)) return;
            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (dependency.GetProperty("dependency_type").GetString() != "required") continue;
                var dependencyId = DependencyId(dependency);
                if (!plan.TryGetValue(dependencyId, out var selected))
                    throw new InvalidDataException("Не разрешена обязательная зависимость проекта " + id);
                if (dependency.TryGetProperty("version_id", out var pin) && pin.ValueKind == JsonValueKind.String)
                {
                    if (selected.GetProperty("id").GetString() != pin.GetString())
                        throw new InvalidDataException("Моды требуют разные версии одной зависимости: " + dependencyId);
                    pinned[dependencyId] = pin.GetString()!;
                }
                ValidateRequired(dependencyId);
            }
        }
        ValidateRequired(projectId);
        foreach (var obsolete in plan.Keys.Where(id => !mandatoryProjects.Contains(id)).ToArray()) plan.Remove(obsolete);
        // Resolve every mandatory sibling before optional branches can introduce version pins.
        for (var index = 0; index < optionalBranches.Count; index++)
        {
            var branch = optionalBranches[index];
            if (!plan.TryGetValue(branch.Origin, out var origin) || origin.GetProperty("id").GetString() != branch.Version) continue;
            var previousPlan = new Dictionary<string, JsonElement>(plan, StringComparer.OrdinalIgnoreCase);
            var previousPins = new Dictionary<string, string>(pinned, StringComparer.OrdinalIgnoreCase);
            var previousCount = optionalBranches.Count;
            try
            {
                var id = await GetDependencyProjectIdAsync(branch.Dependency, cancellationToken);
                if (id.Length == 0) throw new InvalidDataException("Modrinth не вернул ID необязательной зависимости.");
                var pin = branch.Dependency.TryGetProperty("version_id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                await Resolve(id, pin);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                plan = previousPlan; pinned = previousPins;
                optionalBranches.RemoveRange(previousCount, optionalBranches.Count - previousCount);
            }
        }
        string DependencyId(JsonElement dependency)
        {
            if (dependency.TryGetProperty("project_id", out var id) && id.ValueKind == JsonValueKind.String) return id.GetString() ?? "";
            if (dependency.TryGetProperty("version_id", out var version) && version.ValueKind == JsonValueKind.String)
                return plan.FirstOrDefault(entry => entry.Value.GetProperty("id").GetString() == version.GetString()).Key ?? "";
            return "";
        }
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mandatory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id, bool required)
        {
            if (!plan.TryGetValue(id, out var version)) return;
            var first = reachable.Add(id); var promoted = required && mandatory.Add(id);
            if ((!first && !promoted) || dependencyDownloadMode == "none" || !version.TryGetProperty("dependencies", out var dependencies)) return;
            foreach (var dependency in dependencies.EnumerateArray())
            {
                var type = dependency.GetProperty("dependency_type").GetString();
                if (type == "required" || (type == "optional" && dependencyDownloadMode == "all")) Visit(DependencyId(dependency), required && type == "required");
            }
        }
        Visit(projectId, true);
        var requests = plan.Where(entry => reachable.Contains(entry.Key)).Select(entry => new CatalogModInstaller.Request(entry.Key, directory =>
            DownloadPrimaryFileAsync(entry.Value, directory, entry.Key, entry.Key == projectId ? progress : null, cancellationToken), mandatory.Contains(entry.Key),
            dependencyDownloadMode != "none" && entry.Value.TryGetProperty("dependencies", out var dependencies)
                ? dependencies.EnumerateArray().Where(dep => dep.GetProperty("dependency_type").GetString() == "required").Select(DependencyId).Where(id => id.Length > 0).ToArray() : [],
            dependencyDownloadMode == "all" && entry.Value.TryGetProperty("dependencies", out var optional)
                ? optional.EnumerateArray().Where(dep => dep.GetProperty("dependency_type").GetString() == "optional").Select(DependencyId).Where(id => id.Length > 0).ToArray() : [],
            entry.Value.TryGetProperty("dependencies", out var conflicts)
                ? conflicts.EnumerateArray().Where(dep => dep.GetProperty("dependency_type").GetString() == "incompatible"
                    && (!dep.TryGetProperty("version_id", out var pin) || pin.ValueKind != JsonValueKind.String))
                    .Select(DependencyId).Where(id => id.Length > 0).ToArray() : [])).ToArray();
        var results = await CatalogModInstaller.InstallAsync(instance, "modrinth", requests, cancellationToken);
        installed.UnionWith(results.Keys);
        return results[projectId];
    }

	private async Task<JsonDocument> GetVersionsAsync(string projectId, string gameVersion, string loader, CancellationToken cancellationToken)
	{
		List<string> list = new List<string> { "include_changelog=false" };
		if (!string.IsNullOrWhiteSpace(gameVersion))
		{
			list.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new string[1] { gameVersion })));
		}
		if (!string.IsNullOrWhiteSpace(loader))
		{
			list.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new string[1] { loader.ToLowerInvariant() })));
		}
		return await GetJsonAsync($"{_apiBase}/project/{Uri.EscapeDataString(projectId)}/version?{string.Join('&', list)}", cancellationToken);
	}

	public async Task<IReadOnlyList<CatalogVersion>> GetProjectVersionsAsync(string projectId, string gameVersion, string loader, CancellationToken cancellationToken = default(CancellationToken))
	{
		using JsonDocument jsonDocument = await GetVersionsAsync(projectId, gameVersion, loader, cancellationToken);
		JsonElement value;
		JsonElement value2;
		JsonElement value3;
		DateTime value4;
		JsonElement value5;
		JsonElement value6;
		return (from version in jsonDocument.RootElement.EnumerateArray()
			select new CatalogVersion(version.GetProperty("id").GetString() ?? string.Empty, version.TryGetProperty("version_number", out value) ? (value.GetString() ?? string.Empty) : string.Empty, VersionPrimaryFileName(version), version.TryGetProperty("version_type", out value2) ? (value2.GetString() ?? "release") : "release", (version.TryGetProperty("date_published", out value3) && value3.TryGetDateTime(out value4)) ? new DateTime?(value4) : ((DateTime?)null), version.TryGetProperty("game_versions", out value5) ? ((IReadOnlyList<string>)(from item in value5.EnumerateArray()
				select item.GetString() ?? string.Empty).ToArray()) : ((IReadOnlyList<string>)Array.Empty<string>()), version.TryGetProperty("loaders", out value6) ? ((IReadOnlyList<string>)(from item in value6.EnumerateArray()
				select item.GetString() ?? string.Empty).ToArray()) : ((IReadOnlyList<string>)Array.Empty<string>()))).ToArray();
	}

	private static string VersionPrimaryFileName(JsonElement version)
	{
		if (!version.TryGetProperty("files", out var value) || value.GetArrayLength() == 0)
		{
			return string.Empty;
		}
		JsonElement value3;
		JsonElement jsonElement = value.EnumerateArray().FirstOrDefault((JsonElement file) => file.TryGetProperty("primary", out value3) && value3.ValueKind == JsonValueKind.True);
		if (jsonElement.ValueKind == JsonValueKind.Undefined)
		{
			jsonElement = value[0];
		}
		if (!jsonElement.TryGetProperty("filename", out var value2))
		{
			return string.Empty;
		}
		return value2.GetString() ?? string.Empty;
	}

	private async Task<IReadOnlyList<ModDependencyInfo>> ResolveDependenciesAsync(JsonElement version, CancellationToken cancellationToken)
	{
		if (!version.TryGetProperty("dependencies", out var value))
		{
			return Array.Empty<ModDependencyInfo>();
		}
		List<(string ProjectId, string Title, string Type)> raw = new List<(string, string, string)>();
		foreach (JsonElement dependency in value.EnumerateArray())
		{
			string text = await GetDependencyProjectIdAsync(dependency, cancellationToken);
			JsonElement value2;
			string text2 = ((dependency.TryGetProperty("file_name", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? string.Empty) : string.Empty);
			JsonElement value3;
			string item = (dependency.TryGetProperty("dependency_type", out value3) ? (value3.GetString() ?? "required") : "required");
			if (!string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(text2))
			{
				raw.Add((text, text2, item));
			}
		}
		List<string> list = (from tuple in raw
			select tuple.ProjectId into id
			where !string.IsNullOrWhiteSpace(id)
			select id).Distinct().ToList();
		Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (list.Count > 0)
		{
			using JsonDocument jsonDocument = await GetJsonAsync(_apiBase + "/projects?ids=" + Uri.EscapeDataString(JsonSerializer.Serialize(list)), cancellationToken);
			foreach (JsonElement item2 in jsonDocument.RootElement.EnumerateArray())
			{
				string text3 = item2.GetProperty("id").GetString() ?? string.Empty;
				string value4 = item2.GetProperty("title").GetString() ?? text3;
				if (text3.Length > 0)
				{
					titles[text3] = value4;
				}
			}
		}
		return raw.Select<(string, string, string), ModDependencyInfo>(delegate((string ProjectId, string Title, string Type) tuple2)
		{
			var (projectId, _, _) = tuple2;
			string title;
			if (!titles.TryGetValue(tuple2.ProjectId, out var value5))
			{
				if (tuple2.Title.Length <= 0)
				{
					(title, _, _) = tuple2;
				}
				else
				{
					title = tuple2.Title;
				}
			}
			else
			{
				title = value5;
			}
			return new ModDependencyInfo(projectId, title, tuple2.Type, DependencyTypeDisplay(tuple2.Type));
		}).ToList();
	}

	private async Task<string> GetDependencyProjectIdAsync(JsonElement dependency, CancellationToken cancellationToken)
	{
		if (dependency.TryGetProperty("project_id", out var value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? string.Empty;
		}
		if (!dependency.TryGetProperty("version_id", out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		using JsonDocument jsonDocument = await GetJsonAsync(_apiBase + "/version/" + Uri.EscapeDataString(value2.GetString() ?? ""), cancellationToken);
		JsonElement value3;
		return jsonDocument.RootElement.TryGetProperty("project_id", out value3) ? (value3.GetString() ?? string.Empty) : string.Empty;
	}

	private async Task<string> DownloadPrimaryFileAsync(JsonElement version, string destinationDirectory, string fallbackName, IProgress<double>? progress, CancellationToken cancellationToken)
	{
		List<JsonElement> list = version.GetProperty("files").EnumerateArray().ToList();
		if (list.Count == 0)
		{
			throw new InvalidDataException("Modrinth не вернул файл для скачивания.");
		}
		JsonElement value2;
		JsonElement file = list.FirstOrDefault((JsonElement item) => item.TryGetProperty("primary", out value2) && value2.GetBoolean());
		if (file.ValueKind == JsonValueKind.Undefined)
		{
			file = list[0];
		}
		string path = SanitizeFileName(file.GetProperty("filename").GetString() ?? fallbackName);
		string url = file.GetProperty("url").GetString() ?? throw new InvalidDataException("Modrinth не вернул ссылку на файл.");
		string destination = Path.Combine(destinationDirectory, path);
		string? hash = file.TryGetProperty("hashes", out var value) && value.TryGetProperty("sha512", out var sha512) ? sha512.GetString() : null;
		await DownloadFileAsync(url, destination, progress, cancellationToken, hash);
		return destination;
	}

	private async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken, string? expectedSha512 = null)
	{
		string temporary = destination + ".part-" + Guid.NewGuid().ToString("N");
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();
			long? length = response.Content.Headers.ContentLength;
			await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
			await using (FileStream output = File.Create(temporary))
			{
				byte[] buffer = new byte[131072];
				long total = 0L;
				while (true)
				{
					int num;
					int read = (num = await input.ReadAsync(buffer, cancellationToken));
					if (num <= 0)
					{
						break;
					}
					await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
					total += read;
					if (length > 0)
					{
						progress?.Report((double)total * 100.0 / (double)length.Value);
					}
				}
			}
			if (expectedSha512 is not null)
			{
				await using var checkStream = File.OpenRead(temporary);
				if (!Convert.ToHexString(await SHA512.HashDataAsync(checkStream, cancellationToken)).Equals(expectedSha512, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("Контрольная сумма загруженного файла не совпала.");
			}
			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporary, destination, overwrite: true);
		}
		catch
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
			throw;
		}
	}

	private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
		response.EnsureSuccessStatusCode();
		JsonDocument result;
		await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
		{
			result = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
		}
		return result;
	}

	private static string DestinationDirectory(string projectType, GameInstance instance)
	{
		return projectType switch
		{
			"resourcepack" => Path.Combine(instance.DirectoryPath, "resourcepacks"), 
			"shader" => Path.Combine(instance.DirectoryPath, "shaderpacks"), 
			"datapack" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "datapacks"), 
			"modpack" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads", "modpacks"), 
			_ => instance.ModsPath, 
		};
	}

	private static string EffectiveLoader(string projectType, string loader)
	{
		if (!(projectType == "datapack"))
		{
			if (projectType == "resourcepack")
			{
				return string.IsNullOrWhiteSpace(loader) ? "minecraft" : loader;
			}
			return loader;
		}
		return "datapack";
	}

	private static int LoaderOrder(string loader)
	{
		return loader switch
		{
			"fabric" => 0, 
			"forge" => 1, 
			"neoforge" => 2, 
			"quilt" => 3, 
			"minecraft" => 4, 
			"iris" => 5, 
			"optifine" => 6, 
			_ => 100, 
		};
	}

	private static string LoaderDisplayName(string loader)
	{
		return loader switch
		{
			"fabric" => "Fabric", 
			"forge" => "Forge", 
			"neoforge" => "NeoForge", 
			"quilt" => "Quilt", 
			"minecraft" => "Minecraft", 
			"datapack" => "Data Pack", 
			"iris" => "Iris", 
			"optifine" => "OptiFine", 
			_ => loader, 
		};
	}

	private static string DependencyTypeDisplay(string type)
	{
		return type switch
		{
			"required" => "Обязательная", 
			"optional" => "Необязательная", 
			"incompatible" => "Несовместимая", 
			"embedded" => "Встроенная", 
			_ => type, 
		};
	}

	private static string CategoryDisplayName(string category)
	{
		return category switch
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
			_ => category.Replace('-', ' '), 
		};
	}

	private static string SanitizeFileName(string fileName)
	{
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			fileName = fileName.Replace(oldChar, '_');
		}
		return fileName;
	}
}
