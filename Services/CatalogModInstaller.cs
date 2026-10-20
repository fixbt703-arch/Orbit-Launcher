using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using OrbitLauncher.Models;

namespace OrbitLauncher.Services;

/// <summary>Stages catalog downloads before replacing identified mods; old files remain recoverable.</summary>
public static class CatalogModInstaller
{
    public sealed record Request(string ProjectId, Func<string, Task<string>> Download, bool Required = true,
        string[]? RequiredProjects = null, string[]? OptionalProjects = null, string[]? IncompatibleProjects = null,
        Func<string, CancellationToken, Task<IReadOnlyList<Request>>>? ManifestDependencies = null);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IReadOnlyDictionary<string, string>> InstallAsync(GameInstance instance, string source,
        IReadOnlyList<Request> requests, CancellationToken token)
    {
        var root = Path.GetFullPath(instance.DirectoryPath);
        var gate = Gates.GetOrAdd(root, _ => new(1, 1));
        await gate.WaitAsync(token);
        var job = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(root, ".orbit", "catalog-staging", job);
        var backup = Path.Combine(root, ".orbit", "mod-backups", job);
        var moved = new List<(string Original, string Backup)>();
        var published = new List<string>();
        var pending = requests.ToList();
        var downloadFailures = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
        try
        {
            AssertNoLinks(stage); AssertNoLinks(instance.ModsPath); AssertNoLinks(backup);
            Directory.CreateDirectory(stage); Directory.CreateDirectory(instance.ModsPath);
            var staged = new List<(string ProjectId, string Path, HashSet<string> Ids)>();
            for (var index = 0; index < pending.Count; index++)
            {
                var request = pending[index];
                token.ThrowIfCancellationRequested();
                var folder = Path.Combine(stage, staged.Count.ToString()); Directory.CreateDirectory(folder);
                string path;
                HashSet<string> ids;
                try
                {
                    path = await request.Download(folder);
                    if (Path.GetDirectoryName(Path.GetFullPath(path)) != folder) throw new IOException("Некорректный путь загрузки мода.");
                    if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Каталог вернул файл, который не является JAR-модом.");
                    // Reject an HTML error page or damaged archive even if the catalog omitted hashes.
                    using (var archive = ZipFile.OpenRead(path)) { if (archive.Entries.Count == 0) throw new InvalidDataException("Загружен пустой JAR: " + request.ProjectId); }
                    ids = ReadIds(path);
                    if (request.ManifestDependencies is not null)
                    {
                        var discovered = await request.ManifestDependencies(path, token);
                        pending[index] = request with {
                            RequiredProjects = (request.RequiredProjects ?? []).Concat(discovered.Where(item => item.Required).Select(item => item.ProjectId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                            OptionalProjects = (request.OptionalProjects ?? []).Concat(discovered.Where(item => !item.Required).Select(item => item.ProjectId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
                        foreach (var dependency in discovered)
                        {
                            var existing = pending.FindIndex(item => item.ProjectId.Equals(dependency.ProjectId, StringComparison.OrdinalIgnoreCase));
                            if (existing < 0) pending.Add(dependency with { Required = request.Required && dependency.Required });
                            else if (request.Required && dependency.Required) pending[existing] = pending[existing] with { Required = true };
                        }
                        if (pending.Count > 512) throw new InvalidDataException("Слишком большое дерево зависимостей модов.");
                    }
                }
                catch (Exception ex) when (!request.Required && ex is not OperationCanceledException) { downloadFailures[request.ProjectId] = ex; continue; }
                staged.Add((request.ProjectId, path, ids));
            }
            // An optional mod is omitted as a unit when one of its required dependencies failed.
            while (true)
            {
                var available = staged.Select(file => file.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var removed = staged.RemoveAll(file => pending.Any(request => request.ProjectId == file.ProjectId
                    && !request.Required && request.RequiredProjects?.Any(id => !available.Contains(id)) == true));
                if (removed == 0) break;
            }
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string id)
            {
                if (!staged.Any(file => file.ProjectId.Equals(id, StringComparison.OrdinalIgnoreCase)) || !reachable.Add(id)) return;
                var request = pending.First(item => item.ProjectId.Equals(id, StringComparison.OrdinalIgnoreCase));
                foreach (var dependency in (request.RequiredProjects ?? []).Concat(request.OptionalProjects ?? [])) Visit(dependency);
            }
            foreach (var request in pending.Where(request => request.Required))
            {
                if (!staged.Any(file => file.ProjectId.Equals(request.ProjectId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Не удалось установить обязательную зависимость " + request.ProjectId, downloadFailures.GetValueOrDefault(request.ProjectId));
                if (request.RequiredProjects?.Any(id => !staged.Any(file => file.ProjectId.Equals(id, StringComparison.OrdinalIgnoreCase))) == true)
                    throw new InvalidDataException("Не загружены обязательные зависимости проекта " + request.ProjectId);
                Visit(request.ProjectId);
            }
            staged.RemoveAll(file => !reachable.Contains(file.ProjectId));
            var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in staged)
                if (!destinations.Add(Path.GetFileName(file.Path)) || file.Ids.Any(id => !allIds.Add(id)))
                    throw new InvalidDataException("Выбранные моды содержат конфликтующие файлы или одинаковые mod ID.");

            var registry = new InstalledContentRegistry();
            var records = registry.Load(instance);
            var oldFiles = InstanceArchive.RegularFiles(instance.ModsPath)
                .Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).ToArray();
            var replacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var oldIds = oldFiles.ToDictionary(path => path, ReadIds, StringComparer.OrdinalIgnoreCase);
            foreach (var file in staged)
            {
                var destination = Path.Combine(instance.ModsPath, Path.GetFileName(file.Path));
                foreach (var old in oldFiles)
                {
                    var knownProject = records.Any(r => r.SourceId == source && r.ProjectId == file.ProjectId
                        && Path.GetFullPath(Path.Combine(root, r.RelativePath)).Equals(old, StringComparison.OrdinalIgnoreCase));
                    var sameMod = file.Ids.Count > 0 && file.Ids.Overlaps(oldIds[old]);
                    if (!knownProject && !sameMod && !old.Equals(destination, StringComparison.OrdinalIgnoreCase)) continue;
                    if (old.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Включите необходимый мод перед установкой: " + Path.GetFileName(old));
                    replacements.Add(old);
                }
            }
            foreach (var request in pending.Where(request => reachable.Contains(request.ProjectId)))
                foreach (var incompatible in request.IncompatibleProjects ?? [])
                    if (reachable.Contains(incompatible) || records.Any(record => record.SourceId.Equals(source, StringComparison.OrdinalIgnoreCase)
                        && record.ProjectId.Equals(incompatible, StringComparison.OrdinalIgnoreCase)
                        && oldFiles.Any(path => !replacements.Contains(path) && !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                            && path.Equals(Path.GetFullPath(Path.Combine(root, record.RelativePath)), StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidDataException($"Мод {request.ProjectId} несовместим с установленным проектом {incompatible}. Состав сборки не изменён.");
            ValidateForgeDependencyVersions(oldFiles.Where(path => !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)).ToArray(),
                staged.Select(file => file.Path).ToArray(), replacements);
            token.ThrowIfCancellationRequested();
            // Commit without cancellation; roll back the entire batch if a file or registry write fails.
            try
            {
                foreach (var old in replacements)
                {
                    var saved = Path.Combine(backup, Path.GetRelativePath(instance.ModsPath, old));
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Move(old, saved); moved.Add((old, saved));
                }
                var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in staged)
                {
                    var destination = Path.Combine(instance.ModsPath, Path.GetFileName(file.Path));
                    File.Move(file.Path, destination); published.Add(destination); results.Add(file.ProjectId, destination);
                }
                await registry.MarkManyAsync(instance, results.Select(file =>
                    (new ModSearchResult { SourceId = source, ProjectId = file.Key, ProjectType = "mod", Title = Path.GetFileNameWithoutExtension(file.Value) }, file.Value)).ToArray());
                return results;
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                foreach (var path in published.AsEnumerable().Reverse())
                    try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failures.Add(ex); }
                foreach (var file in moved.AsEnumerable().Reverse())
                    try { File.Move(file.Backup, file.Original); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failures.Add(ex); }
                if (failures.Count > 1) throw new AggregateException("Не удалось полностью восстановить моды. Резервные файлы: " + backup, failures);
                throw;
            }
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { gate.Release(); }
        }
    }

    private static void ValidateForgeDependencyVersions(string[] oldFiles, string[] stagedFiles, HashSet<string> replacements)
    {
        ForgeModManifest Read(string path)
        {
            try { return ForgeModManifest.Read(path); }
            catch (InvalidDataException) { return new(new Dictionary<string, string>(), []); }
        }
        var before = oldFiles.ToDictionary(path => path, Read, StringComparer.OrdinalIgnoreCase);
        var after = before.Where(file => !replacements.Contains(file.Key)).ToDictionary(file => file.Key, file => file.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var path in stagedFiles) after[path] = Read(path);
        bool HasVersionConflict(ForgeModManifest.Dependency dependency, IEnumerable<ForgeModManifest> files)
        {
            var versions = files.Where(file => file.Mods.ContainsKey(dependency.Id)).Select(file => file.Mods[dependency.Id]).ToArray();
            return versions.Length > 0 && versions.All(version => version.Length > 0 && !ForgeModManifest.Matches(version, dependency.Range));
        }
        foreach (var file in after)
            foreach (var dependency in file.Value.Dependencies.Where(dependency => dependency.Required && dependency.Side != "SERVER"))
                if (HasVersionConflict(dependency, after.Values) && (stagedFiles.Contains(file.Key, StringComparer.OrdinalIgnoreCase)
                    || !HasVersionConflict(dependency, before.Values)))
                    throw new InvalidDataException($"{Path.GetFileName(file.Key)} требует {dependency.Id} {dependency.Range}. Выбранная версия библиотеки нарушает это требование; состав сборки не изменён.");
    }

    private static void AssertNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Установка модов через ссылку на каталог запрещена: " + current);
    }

    private static HashSet<string> ReadIds(string path)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json" })
            {
                var entry = zip.GetEntry(name); if (entry is null || entry.Length > 2_000_000) continue;
                using var stream = entry.Open(); using var json = JsonDocument.Parse(stream);
                var metadata = json.RootElement;
                if (name == "quilt.mod.json" && !metadata.TryGetProperty("quilt_loader", out metadata)) continue;
                if (metadata.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } value) ids.Add(value);
            }
            ids.UnionWith(ForgeModManifest.Read(path).Mods.Keys);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException) { }
        return ids;
    }
}
