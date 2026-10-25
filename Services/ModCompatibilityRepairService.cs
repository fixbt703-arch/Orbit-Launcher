using System.Security.Cryptography;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed record RepairUpdate(int Completed, int Total, string File, string Message, RepairState State);
public enum RepairState { Checking, Replaced, Installed, Disabled, Compatible, NeedsAttention, Failed }
public sealed record RepairSummary(int Checked, int Replaced, int Installed, int Disabled, int NeedsAttention);

/// <summary>Repairs only files that Modrinth can identify by their actual SHA-1 hash.</summary>
public sealed partial class ModCompatibilityRepairService(HttpClient httpClient, string apiBase, string? runtimeRoot = null,
    string? curseForgeApiBase = null, string? curseForgeApiKey = null)
{
    private readonly string _apiBase = apiBase.TrimEnd('/');
    private readonly string _runtimeRoot = runtimeRoot ?? Path.Combine(AppPaths.Minecraft, "runtime");
    private readonly CurseForgeService _curseForge = new(httpClient, curseForgeApiBase, curseForgeApiKey);

    public async Task<RepairSummary> RepairAsync(GameInstance instance, IProgress<RepairUpdate> progress,
        CancellationToken token = default)
    {
        if (instance.Loader is not ("Fabric" or "Forge" or "NeoForge" or "Quilt"))
            throw new InvalidOperationException(L("Выберите сборку с загрузчиком модов.", "Select an instance with a mod loader."));
        Directory.CreateDirectory(instance.ModsPath);
        var files = Directory.EnumerateFiles(instance.ModsPath, "*.jar", SearchOption.TopDirectoryOnly).ToList();
        var checkedCount = 0;
        var replaced = 0;
        var installed = 0;
        var disabled = 0;
        var attention = 0;
        var backupRoot = Path.Combine(instance.DirectoryPath, ".orbit", "repair-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var knownProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var identified = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
        var identificationErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var registryRecords = new InstalledContentRegistry().Load(instance);

        // Identify the whole folder first so an existing dependency later in the list is not installed twice.
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new(0, files.Count, Path.GetFileName(file), L("Определяем установленные проекты…", "Identifying installed projects…"), RepairState.Checking));
            try
            {
                var hash = await Sha1Async(file, token);
                using var version = await FindVersionAsync(hash, token);
                identified[file] = version?.RootElement.Clone();
                if (identified[file] is JsonElement found)
                {
                    var id = RequiredString(found, "project_id");
                    if (id.Length > 0)
                    {
                        knownProjects.Add(id);
                        projectCounts[id] = projectCounts.GetValueOrDefault(id) + 1;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { identificationErrors[file] = ex.Message; }
        }

        // Newly installed dependencies join the queue so their own requirements are repaired.
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            token.ThrowIfCancellationRequested();
            checkedCount++;
            var name = Path.GetFileName(file);
            progress.Report(new(checkedCount, files.Count, name, L("Проверка файла и его версии…", "Checking file and version…"), RepairState.Checking));
            try
            {
                if (identificationErrors.TryGetValue(file, out var identificationError))
                    throw new InvalidOperationException(identificationError);
                if (identified[file] is not JsonElement current)
                {
                    var curseRecord = registryRecords.FirstOrDefault(item => item.SourceId == "curseforge" &&
                        string.Equals(Path.GetFullPath(Path.Combine(instance.DirectoryPath, item.RelativePath)), file, StringComparison.OrdinalIgnoreCase));
                    if (curseRecord is not null && _curseForge.IsConfigured)
                    {
                        var state = await RepairCurseForgeFileAsync(instance, file, curseRecord, backupRoot, progress, checkedCount, files.Count, token);
                        if (state == RepairState.Replaced) replaced++;
                        if (state == RepairState.NeedsAttention) attention++;
                        continue;
                    }
                    attention++;
                    progress.Report(new(checkedCount, files.Count, name, L("Не найден в доступном каталоге. Файл оставлен без изменений.", "Not found in an available catalog. The file was left unchanged."), RepairState.NeedsAttention));
                    continue;
                }
                var projectId = RequiredString(current, "project_id");
                if (projectId.Length == 0)
                    throw new InvalidDataException(L("Каталог не вернул ID проекта.", "The catalog did not return a project ID."));
                if (projectCounts.GetValueOrDefault(projectId) > 1)
                {
                    attention++;
                    progress.Report(new(checkedCount, files.Count, name, L("Несколько включённых файлов одного мода. Отключите дубликаты вручную.", "Multiple enabled files for this mod. Disable duplicates manually."), RepairState.NeedsAttention));
                    continue;
                }
                if (!processedProjects.Add(projectId))
                {
                    attention++;
                    progress.Report(new(checkedCount, files.Count, name, L("Обнаружен второй файл того же проекта. Проверьте дубликаты вручную.", "Another file from this project was found. Review duplicates manually."), RepairState.NeedsAttention));
                    continue;
                }

                JsonElement chosen = current;
                var compatible = Supports(chosen, instance);
                if (!compatible)
                {
                    progress.Report(new(checkedCount, files.Count, name, L("Ищем выпуск для выбранной сборки…", "Finding a release for this instance…"), RepairState.Checking));
                    using var versions = await GetCompatibleVersionsAsync(projectId, instance, token);
                    var selected = versions.RootElement.EnumerateArray()
                        .Where(v => Supports(v, instance))
                        .OrderBy(v => ChannelRank(RequiredString(v, "version_type")))
                        .FirstOrDefault();
                    chosen = selected.ValueKind == JsonValueKind.Object ? selected.Clone() : default;
                    if (chosen.ValueKind != JsonValueKind.Object)
                    {
                        attention++;
                        progress.Report(new(checkedCount, files.Count, name, L("Совместимый выпуск не найден. Старый файл сохранён.", "No compatible release found. The old file was kept."), RepairState.NeedsAttention));
                        continue;
                    }
                    var candidate = PrimaryFile(chosen);
                    if (candidate.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException(L("У совместимого выпуска нет JAR-файла.", "The compatible release has no JAR file."));
                    var destinationName = SafeJarName(RequiredString(candidate, "filename"));
                    if (destinationName.Length == 0)
                        throw new InvalidDataException(L("Каталог вернул небезопасное имя файла.", "The catalog returned an unsafe file name."));
                    var destination = Path.Combine(instance.ModsPath, destinationName);
                    if (!string.Equals(file, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                    {
                        attention++;
                        progress.Report(new(checkedCount, files.Count, name, L("Совместимый JAR уже есть в папке. Проверьте дубликаты вручную.", "The compatible JAR is already in the folder. Review duplicates manually."), RepairState.NeedsAttention));
                        continue;
                    }
                    progress.Report(new(checkedCount, files.Count, name, L("Загрузка и проверка SHA-512…", "Downloading and verifying SHA-512…"), RepairState.Checking));
                    var staged = await DownloadVerifiedAsync(candidate, instance.ModsPath, token);
                    try
                    {
                        Directory.CreateDirectory(backupRoot);
                        var backup = UniquePath(backupRoot, name);
                        File.Move(file, backup);
                        try { File.Move(staged, destination); }
                        catch { File.Move(backup, file); throw; }
                    }
                    finally { if (File.Exists(staged)) File.Delete(staged); }
                    replaced++;
                    progress.Report(new(checkedCount, files.Count, name, LocalizationService.IsEnglish
                        ? $"Replaced with {destinationName}. Previous file saved in .orbit/repair-backups."
                        : $"Заменён на {destinationName}. Предыдущий файл сохранён в .orbit/repair-backups.", RepairState.Replaced));
                    try { await UpdateRegistryAsync(instance, projectId, destination); }
                    catch (Exception ex)
                    {
                        attention++;
                        progress.Report(new(checkedCount, files.Count, name, LocalizationService.IsEnglish
                            ? $"File replaced, but the catalog record could not be updated: {ex.Message}"
                            : $"Файл заменён, но не удалось обновить запись каталога: {ex.Message}", RepairState.NeedsAttention));
                    }
                }
                else
                {
                    progress.Report(new(checkedCount, files.Count, name, L("Версия игры и загрузчик совместимы.", "Game and loader versions are compatible."), RepairState.Compatible));
                }

                // The selected version may require other Modrinth projects. Install only missing projects;
                // leave existing user-managed versions untouched and report them separately on the next scan.
                if (chosen.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dependency in dependencies.EnumerateArray())
                    {
                        if (RequiredString(dependency, "dependency_type") != "required") continue;
                        var dependencyId = RequiredString(dependency, "project_id");
                        var requiredVersionId = RequiredString(dependency, "version_id");
                        try
                        {
                            if (dependencyId.Length == 0 && requiredVersionId.Length > 0)
                            {
                                using var metadata = await GetVersionByIdAsync(requiredVersionId, token);
                                dependencyId = RequiredString(metadata.RootElement, "project_id");
                            }
                            if (dependencyId.Length == 0) throw new InvalidDataException(L("Каталог не вернул ID зависимости.", "The catalog did not return a dependency project ID."));
                            if (knownProjects.Contains(dependencyId)) continue;
                            if (installed >= 512) throw new InvalidDataException(L("Слишком большое дерево зависимостей модов.", "The mod dependency tree is too large."));
                            using var versions = requiredVersionId.Length > 0
                                ? await GetVersionByIdAsync(requiredVersionId, token)
                                : await GetCompatibleVersionsAsync(dependencyId, instance, token);
                            var version = requiredVersionId.Length > 0 ? versions.RootElement
                                : versions.RootElement.EnumerateArray().FirstOrDefault(v => Supports(v, instance));
                            if (version.ValueKind != JsonValueKind.Object || !Supports(version, instance))
                                throw new InvalidDataException(L("требуемый выпуск несовместим со сборкой", "required release is incompatible with this instance"));
                            if (requiredVersionId.Length > 0 && !RequiredString(version, "project_id").Equals(dependencyId, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException(L("версия принадлежит другому проекту", "version belongs to another project"));
                            var candidate = version.ValueKind == JsonValueKind.Object ? PrimaryFile(version) : default;
                            if (candidate.ValueKind != JsonValueKind.Object)
                                throw new InvalidDataException(L("совместимый выпуск не найден", "no compatible release found"));
                            var dependencyName = SafeJarName(RequiredString(candidate, "filename"));
                            if (dependencyName.Length == 0 || File.Exists(Path.Combine(instance.ModsPath, dependencyName)))
                                throw new InvalidDataException(L("требуется ручная проверка файла", "the file needs manual review"));
                            var staged = await DownloadVerifiedAsync(candidate, instance.ModsPath, token);
                            try { File.Move(staged, Path.Combine(instance.ModsPath, dependencyName)); }
                            finally { if (File.Exists(staged)) File.Delete(staged); }
                            knownProjects.Add(dependencyId);
                            var dependencyPath = Path.Combine(instance.ModsPath, dependencyName);
                            files.Add(dependencyPath);
                            identified[dependencyPath] = version.Clone();
                            installed++;
                            progress.Report(new(checkedCount, files.Count, dependencyName, L("Установлена обязательная зависимость.", "Required dependency installed."), RepairState.Installed));
                            try { await UpdateRegistryAsync(instance, dependencyId, Path.Combine(instance.ModsPath, dependencyName)); }
                            catch (Exception ex)
                            {
                                attention++;
                                progress.Report(new(checkedCount, files.Count, dependencyName, LocalizationService.IsEnglish
                                    ? $"Installed, but the catalog record could not be updated: {ex.Message}"
                                    : $"Установлена, но не удалось обновить запись каталога: {ex.Message}", RepairState.NeedsAttention));
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            attention++;
                            progress.Report(new(checkedCount, files.Count, name, LocalizationService.IsEnglish
                                ? $"Dependency {dependencyId}: {ex.Message}." : $"Зависимость {dependencyId}: {ex.Message}.", RepairState.NeedsAttention));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                attention++;
                progress.Report(new(checkedCount, files.Count, name, LocalizationService.IsEnglish
                    ? $"Repair failed: {ex.Message}" : $"Не удалось исправить: {ex.Message}", RepairState.Failed));
            }
        }
        if (instance.Loader == "Fabric")
        {
            var fabric = await RepairFabricConflictsAsync(instance, progress, token);
            replaced += fabric.Replaced;
            disabled += fabric.Disabled;
            attention += fabric.NeedsAttention;
        }
        return new RepairSummary(checkedCount, replaced, installed, disabled, attention);
    }

    private async Task<JsonDocument?> FindVersionAsync(string sha1, CancellationToken token)
    {
        using var response = await httpClient.GetAsync($"{_apiBase}/version_file/{sha1}?algorithm=sha1", token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    private async Task<JsonDocument> GetCompatibleVersionsAsync(string id, GameInstance instance, CancellationToken token)
    {
        var url = $"{_apiBase}/project/{Uri.EscapeDataString(id)}/version?game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.GameVersion }))}&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.Loader.ToLowerInvariant() }))}";
        using var response = await httpClient.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    private async Task<JsonDocument> GetVersionByIdAsync(string id, CancellationToken token)
    {
        using var response = await httpClient.GetAsync($"{_apiBase}/version/{Uri.EscapeDataString(id)}", token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    private static bool Supports(JsonElement version, GameInstance instance) =>
        Contains(version, "game_versions", instance.GameVersion) && Contains(version, "loaders", instance.Loader);

    private static bool Contains(JsonElement version, string key, string value) =>
        version.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array &&
        values.EnumerateArray().Any(item => string.Equals(item.GetString(), value, StringComparison.OrdinalIgnoreCase));

    private static JsonElement PrimaryFile(JsonElement version)
    {
        if (!version.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return default;
        return files.EnumerateArray().Where(f => SafeJarName(RequiredString(f, "filename")).Length > 0)
            .OrderByDescending(f => f.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True)
            .FirstOrDefault();
    }

    private async Task<string> DownloadVerifiedAsync(JsonElement file, string directory, CancellationToken token)
    {
        var url = new Uri(RequiredString(file, "url"));
        if (url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L("Неподдерживаемый адрес загрузки.", "Unsupported download address."));
        var expected = file.GetProperty("hashes").GetProperty("sha512").GetString();
        if (expected is null || expected.Length != 128) throw new InvalidDataException(L("Нет контрольной суммы SHA-512.", "SHA-512 checksum is missing."));
        var temporary = Path.Combine(directory, ".orbit-repair-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var target = File.Create(temporary))
                await source.CopyToAsync(target, token);
            await using var verified = File.OpenRead(temporary);
            var actual = Convert.ToHexString(await SHA512.HashDataAsync(verified, token));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L("Контрольная сумма загруженного файла не совпала.", "Downloaded file checksum did not match."));
            return temporary;
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }

    private static async Task<string> Sha1Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static string SafeJarName(string name) => name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
        && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? name : string.Empty;

    private static int ChannelRank(string channel) => channel switch { "release" => 0, "beta" => 1, _ => 2 };

    private static string UniquePath(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path)) return path;
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + "-" + name);
    }

    private static string L(string ru, string en) => LocalizationService.IsEnglish ? en : ru;

    private static async Task UpdateRegistryAsync(GameInstance instance, string projectId, string path, string sourceId = "modrinth")
    {
        var registry = new InstalledContentRegistry();
        var old = registry.Load(instance).FirstOrDefault(item => item.SourceId == sourceId && item.ProjectId == projectId);
        await registry.MarkAsync(instance,
            new ModSearchResult { SourceId = sourceId, ProjectId = projectId, ProjectType = "mod",
                Title = old?.Title ?? Path.GetFileNameWithoutExtension(path), IconUrl = old?.IconUrl ?? string.Empty },
            CatalogInstallState.Installed, path);
    }
}
