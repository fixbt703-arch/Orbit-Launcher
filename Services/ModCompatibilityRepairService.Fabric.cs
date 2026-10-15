using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed partial class ModCompatibilityRepairService
{
    private sealed record FabricMod(string Source, string Id, string Version, IReadOnlyList<string> Provides,
        IReadOnlyDictionary<string, string[]> Depends, IReadOnlyDictionary<string, string[]> Breaks);
    private sealed record FabricIssue(string Origin, string? Target, string Description, string Key, int RequiredJava = 0);
    private sealed record FabricRepairResult(int Replaced, int Disabled, int NeedsAttention);

    private async Task<FabricRepairResult> RepairFabricConflictsAsync(GameInstance instance, IProgress<RepairUpdate> progress,
        CancellationToken token)
    {
        var replaced = 0;
        var disabled = 0;
        var attention = 0;
        var backups = Path.Combine(instance.DirectoryPath, ".orbit", "repair-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var javaMajor = CurrentJavaMajor(instance);
        var mods = ReadFabricMods(instance.ModsPath);
        var issues = FindFabricIssues(mods, instance, javaMajor);
        var requiredJava = issues.Select(issue => issue.RequiredJava).DefaultIfEmpty(0).Max();
        if (requiredJava > javaMajor)
        {
            progress.Report(new(0, Math.Max(1, mods.Count), "Java", L("Ищем подходящую Java…", "Finding a suitable Java runtime…"), RepairState.Checking));
            var javaPath = FindJavaRuntime(requiredJava);
            if (javaPath is not null)
            {
                instance.JavaPath = javaPath;
                await SaveJavaSelectionAsync(instance);
                javaMajor = CurrentJavaMajor(instance);
                progress.Report(new(0, Math.Max(1, mods.Count), "Java", LocalizationService.IsEnglish
                    ? $"Selected Java {javaMajor} for this instance."
                    : $"Для сборки выбрана Java {javaMajor}.", RepairState.Replaced));
            }
        }

        var registry = new InstalledContentRegistry().Load(instance);
        for (var round = 0; round < 16; round++)
        {
            token.ThrowIfCancellationRequested();
            mods = ReadFabricMods(instance.ModsPath);
            issues = FindFabricIssues(mods, instance, javaMajor);
            if (issues.Count == 0) break;
            progress.Report(new(0, Math.Max(1, mods.Count), "Fabric", LocalizationService.IsEnglish
                ? $"Checking {issues.Count} dependency conflicts…" : $"Проверяем конфликты зависимостей: {issues.Count}…", RepairState.Checking));

            var candidatePaths = issues.SelectMany(issue => new[] { issue.Target, issue.Origin })
                .Where(path => path is not null && File.Exists(path))
                .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var improved = false;
            foreach (var path in candidatePaths)
            {
                token.ThrowIfCancellationRequested();
                var record = registry.FirstOrDefault(item => item.ProjectType == "mod" &&
                    string.Equals(Path.GetFullPath(Path.Combine(instance.DirectoryPath, item.RelativePath)), path, StringComparison.OrdinalIgnoreCase));
                var originalIds = mods.Where(mod => mod.Source.Equals(path, StringComparison.OrdinalIgnoreCase))
                    .Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (record?.SourceId == "curseforge" && _curseForge.IsConfigured)
                {
                    try
                    {
                        var versions = await _curseForge.GetProjectVersionsAsync(record.ProjectId, instance.GameVersion, instance.Loader, token);
                        foreach (var version in versions.Where(version => version.GameVersions.Contains(instance.GameVersion, StringComparer.OrdinalIgnoreCase)
                                     && (version.Loaders.Count == 0 || version.Loaders.Contains(instance.Loader, StringComparer.OrdinalIgnoreCase)))
                                 .OrderBy(version => ChannelRank(version.Channel)).ThenByDescending(version => version.PublishedAt).Take(12))
                        {
                            if (!int.TryParse(version.Id, out var fileId) || version.FileName.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
                                || SafeJarName(version.FileName).Length == 0 || File.Exists(Path.Combine(instance.ModsPath, version.FileName))) continue;
                            var staging = Path.Combine(instance.DirectoryPath, ".orbit", "repair-staging", Guid.NewGuid().ToString("N"));
                            try
                            {
                                var staged = await _curseForge.DownloadFileForRepairAsync(record.ProjectId, fileId, staging, token);
                                var proposed = mods.Where(mod => !mod.Source.Equals(path, StringComparison.OrdinalIgnoreCase))
                                    .Concat(ReadFabricFile(staged)).ToList();
                                if (!proposed.Any(mod => originalIds.Contains(mod.Id)) ||
                                    FindFabricIssues(proposed, instance, javaMajor).Count >= issues.Count) continue;
                                Directory.CreateDirectory(backups);
                                var backup = UniquePath(backups, Path.GetFileName(path));
                                var destination = Path.Combine(instance.ModsPath, version.FileName);
                                File.Move(path, backup);
                                try { File.Move(staged, destination); }
                                catch { File.Move(backup, path); throw; }
                                replaced++;
                                improved = true;
                                progress.Report(new(0, Math.Max(1, mods.Count), Path.GetFileName(path), LocalizationService.IsEnglish
                                    ? $"Replaced with {version.FileName} to resolve Fabric dependencies."
                                    : $"Заменён на {version.FileName} для устранения конфликта Fabric.", RepairState.Replaced));
                                try { await UpdateRegistryAsync(instance, record.ProjectId, destination, "curseforge"); }
                                catch { attention++; }
                                break;
                            }
                            catch (OperationCanceledException) { throw; }
                            catch { /* Try the next candidate. */ }
                            finally
                            {
                                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                                catch (IOException) { /* Keep a successful replacement. */ }
                                catch (UnauthorizedAccessException) { /* Leave locked staging data for later cleanup. */ }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Try other projects before disabling a mod. */ }
                    if (improved) break;
                    continue;
                }
                var projectId = record?.ProjectId ?? string.Empty;
                if (projectId.Length == 0)
                {
                    try
                    {
                        using var version = await FindVersionAsync(await Sha1Async(path, token), token);
                        if (version is not null) projectId = RequiredString(version.RootElement, "project_id");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Another source cannot be resolved through Modrinth. */ }
                }
                if (projectId.Length == 0) continue;
                try
                {
                    using var versions = await GetCompatibleVersionsAsync(projectId, instance, token);
                    foreach (var version in versions.RootElement.EnumerateArray().Take(12))
                    {
                        token.ThrowIfCancellationRequested();
                        var file = PrimaryFile(version);
                        if (file.ValueKind != JsonValueKind.Object) continue;
                        var name = SafeJarName(RequiredString(file, "filename"));
                        if (name.Length == 0 || name.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) continue;
                        var destination = Path.Combine(instance.ModsPath, name);
                        if (File.Exists(destination)) continue;
                        string staged;
                        try { staged = await DownloadVerifiedAsync(file, instance.ModsPath, token); }
                        catch (OperationCanceledException) { throw; }
                        catch { continue; }
                        try
                        {
                            var proposed = mods.Where(mod => !mod.Source.Equals(path, StringComparison.OrdinalIgnoreCase))
                                .Concat(ReadFabricFile(staged)).ToList();
                            if (!proposed.Any(mod => originalIds.Contains(mod.Id)) ||
                                FindFabricIssues(proposed, instance, javaMajor).Count >= issues.Count) continue;
                            Directory.CreateDirectory(backups);
                            var backup = UniquePath(backups, Path.GetFileName(path));
                            File.Move(path, backup);
                            try { File.Move(staged, destination); }
                            catch { File.Move(backup, path); throw; }
                            replaced++;
                            improved = true;
                            progress.Report(new(0, Math.Max(1, mods.Count), Path.GetFileName(path), LocalizationService.IsEnglish
                                ? $"Replaced with {name} to resolve Fabric dependencies."
                                : $"Заменён на {name} для устранения конфликта Fabric.", RepairState.Replaced));
                            try { await UpdateRegistryAsync(instance, projectId, destination); }
                            catch { attention++; }
                            break;
                        }
                        finally { if (File.Exists(staged)) File.Delete(staged); }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* Continue with other candidates before disabling a mod. */ }
                if (improved) break;
            }
            if (improved) continue;

            // If no compatible upgrade improves the dependency graph, disable the smallest
            // reversible set one file at a time. Never disable a file if this makes the graph worse.
            string? disablePath = null;
            var bestScore = issues.Count;
            var existingKeys = issues.Select(issue => issue.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in candidatePaths)
            {
                if (File.Exists(path + ".disabled")) continue;
                var remaining = FindFabricIssues(mods.Where(mod => !mod.Source.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList(), instance, javaMajor);
                if (remaining.Any(issue => !existingKeys.Contains(issue.Key))) continue;
                if (remaining.Count < bestScore) { bestScore = remaining.Count; disablePath = path; }
            }
            if (disablePath is null) break;
            File.Move(disablePath, disablePath + ".disabled");
            disabled++;
            progress.Report(new(0, Math.Max(1, mods.Count), Path.GetFileName(disablePath), L(
                "Нет подходящей замены; мод отключён, его файл сохранён с расширением .disabled.",
                "No suitable replacement; mod disabled, with its file retained as .disabled."), RepairState.Disabled));
        }

        mods = ReadFabricMods(instance.ModsPath);
        issues = FindFabricIssues(mods, instance, javaMajor);
        foreach (var issue in issues)
        {
            attention++;
            progress.Report(new(0, Math.Max(1, mods.Count), Path.GetFileName(issue.Origin), issue.Description, RepairState.NeedsAttention));
        }
        return new FabricRepairResult(replaced, disabled, attention);
    }

    private static List<FabricMod> ReadFabricMods(string modsPath) => Directory.Exists(modsPath)
        ? Directory.EnumerateFiles(modsPath, "*.jar", SearchOption.TopDirectoryOnly).SelectMany(ReadFabricFile).ToList()
        : [];

    private static List<FabricMod> ReadFabricFile(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var mods = new List<FabricMod>();
            ReadFabricArchive(archive, path, mods, 0);
            return mods;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { return []; }
    }

    private static void ReadFabricArchive(ZipArchive archive, string source, List<FabricMod> mods, int depth)
    {
        if (depth > 3) return;
        var manifest = archive.GetEntry("fabric.mod.json");
        if (manifest is null || manifest.Length is <= 0 or > 2_000_000) return;
        using var json = JsonDocument.Parse(manifest.Open());
        var root = json.RootElement;
        var id = RequiredString(root, "id");
        if (id.Length == 0) return;
        var provides = root.TryGetProperty("provides", out var provided) && provided.ValueKind == JsonValueKind.Array
            ? provided.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty).Where(value => value.Length > 0).ToArray()
            : [];
        mods.Add(new FabricMod(source, id, RequiredString(root, "version"), provides,
            ReadPredicates(root, "depends"), ReadPredicates(root, "breaks")));
        if (!root.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array) return;
        foreach (var jar in jars.EnumerateArray())
        {
            var path = RequiredString(jar, "file");
            var nested = archive.GetEntry(path);
            if (nested is null || nested.Length is <= 0 or > 64_000_000) continue;
            try
            {
                using var bytes = new MemoryStream();
                using (var input = nested.Open()) input.CopyTo(bytes);
                bytes.Position = 0;
                using var child = new ZipArchive(bytes, ZipArchiveMode.Read);
                ReadFabricArchive(child, source, mods, depth + 1);
            }
            catch (InvalidDataException) { /* Ignore a malformed nested archive. */ }
        }
    }

    private static IReadOnlyDictionary<string, string[]> ReadPredicates(JsonElement root, string key)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in value.EnumerateObject())
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => [property.Value.GetString() ?? "*"],
                JsonValueKind.Array => property.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? "*").ToArray(),
                _ => []
            };
        return result;
    }

    private static List<FabricIssue> FindFabricIssues(List<FabricMod> mods, GameInstance instance, int javaMajor)
    {
        var issues = new List<FabricIssue>();
        var byId = mods.SelectMany(mod => new[] { mod.Id }.Concat(mod.Provides).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => (id, mod)))
            .GroupBy(item => item.id, item => item.mod, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            foreach (var (id, predicates) in mod.Depends)
            {
                if (id.Equals("mixinextras", StringComparison.OrdinalIgnoreCase)) continue; // Bundled by Fabric Loader.
                var present = BuiltinVersion(id, instance, javaMajor);
                var target = BestInstalled(byId.GetValueOrDefault(id));
                if (present is null && target is null)
                {
                    issues.Add(new FabricIssue(mod.Source, null, L($"Не хватает обязательного мода {id} для {mod.Id}.",
                        $"Required mod {id} is missing for {mod.Id}."), $"depends:{mod.Source}:{id}:missing"));
                    continue;
                }
                var actual = present ?? target!.Version;
                if (!Matches(predicates, actual))
                {
                    var requiredJava = id.Equals("java", StringComparison.OrdinalIgnoreCase) ? MinimumJava(predicates) : 0;
                    issues.Add(new FabricIssue(mod.Source, target?.Source, L(
                        $"{mod.Id} требует {id} {string.Join(" или ", predicates)}, установлено {actual}.",
                        $"{mod.Id} requires {id} {string.Join(" or ", predicates)}, found {actual}."),
                        $"depends:{mod.Source}:{id}:version", requiredJava));
                }
            }
            foreach (var (id, predicates) in mod.Breaks)
            {
                var target = BestInstalled(byId.GetValueOrDefault(id));
                if (target is not null && !target.Source.Equals(mod.Source, StringComparison.OrdinalIgnoreCase) && Matches(predicates, target.Version))
                    issues.Add(new FabricIssue(mod.Source, target.Source, L($"{mod.Id} конфликтует с {id} {target.Version}.",
                        $"{mod.Id} conflicts with {id} {target.Version}."), $"breaks:{mod.Source}:{id}"));
            }
        }
        return issues;
    }

    private static FabricMod? BestInstalled(List<FabricMod>? candidates)
    {
        if (candidates is null || candidates.Count == 0) return null;
        return candidates.Aggregate((best, next) =>
        {
            var left = TryNumericVersion(best.Version, out var a) ? a : new int[4];
            var right = TryNumericVersion(next.Version, out var b) ? b : new int[4];
            return Compare(right, left) > 0 ? next : best;
        });
    }

    private static string? BuiltinVersion(string id, GameInstance instance, int javaMajor) => id.ToLowerInvariant() switch
    {
        "minecraft" => instance.GameVersion,
        "fabricloader" => instance.LoaderVersion,
        "java" => javaMajor.ToString(),
        _ => null
    };

    private static bool Matches(string[] predicates, string actual) => predicates.Length == 0 ||
        predicates.Any(predicate => predicate.Split("||", StringSplitOptions.TrimEntries).Any(part => MatchesOne(part, actual)));

    private static bool MatchesOne(string predicate, string actual)
    {
        predicate = predicate.Trim();
        if (predicate is "*" or "") return true;
        if (predicate.EndsWith(".x", StringComparison.OrdinalIgnoreCase) || predicate.EndsWith(".*"))
            return actual.StartsWith(predicate[..^1], StringComparison.OrdinalIgnoreCase);
        var op = new[] { ">=", "<=", ">", "<", "=", "^", "~" }.FirstOrDefault(predicate.StartsWith) ?? "=";
        var target = op == "=" && !predicate.StartsWith('=') ? predicate : predicate[op.Length..];
        if (!TryNumericVersion(actual, out var left) || !TryNumericVersion(target, out var right)) return true;
        var comparison = Compare(left, right);
        return op switch
        {
            ">=" => comparison >= 0, "<=" => comparison <= 0, ">" => comparison > 0, "<" => comparison < 0,
            "^" => comparison >= 0 && left[0] == right[0],
            "~" => comparison >= 0 && left[0] == right[0] && left[1] == right[1],
            _ => comparison == 0
        };
    }

    private static bool TryNumericVersion(string text, out int[] parts)
    {
        parts = [0, 0, 0, 0];
        var match = Regex.Match(text, @"^\d+(?:\.\d+){0,3}");
        if (!match.Success) return false;
        var numbers = match.Value.Split('.');
        for (var i = 0; i < numbers.Length; i++)
            if (!int.TryParse(numbers[i], out parts[i])) return false;
        return true;
    }

    private static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var compared = left[i].CompareTo(right[i]);
            if (compared != 0) return compared;
        }
        return 0;
    }

    private static int MinimumJava(string[] predicates) => predicates.SelectMany(p => Regex.Matches(p, @">=\s*(\d+)")
        .Select(match => int.TryParse(match.Groups[1].Value, out var value) ? value : 0)).DefaultIfEmpty().Max();

    private static int CurrentJavaMajor(GameInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.JavaPath))
        {
            var release = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(instance.JavaPath)!, "..", "release"));
            var major = ReadJavaRelease(release);
            if (major > 0) return major;
        }
        var versionFile = Path.Combine(AppPaths.Minecraft, "versions", instance.GameVersion, instance.GameVersion + ".json");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(versionFile));
            return json.RootElement.GetProperty("javaVersion").GetProperty("majorVersion").GetInt32();
        }
        catch { return 21; }
    }

    private string? FindJavaRuntime(int minimum)
    {
        if (!Directory.Exists(_runtimeRoot)) return null;
        return Directory.EnumerateFiles(_runtimeRoot, "release", SearchOption.AllDirectories)
            .Select(release => (Major: ReadJavaRelease(release), Java: Path.Combine(Path.GetDirectoryName(release)!, "bin", "javaw.exe")))
            .Where(candidate => candidate.Major >= minimum && File.Exists(candidate.Java))
            .OrderBy(candidate => candidate.Major).Select(candidate => candidate.Java).FirstOrDefault();
    }

    private static int ReadJavaRelease(string path)
    {
        try
        {
            var line = File.ReadLines(path).FirstOrDefault(value => value.StartsWith("JAVA_VERSION=", StringComparison.Ordinal));
            var version = line?.Split('"').Skip(1).FirstOrDefault() ?? string.Empty;
            if (version.StartsWith("1.8", StringComparison.Ordinal)) return 8;
            return int.TryParse(version.Split('.')[0], out var major) ? major : 0;
        }
        catch { return 0; }
    }

    private static async Task SaveJavaSelectionAsync(GameInstance instance)
    {
        var destination = Path.Combine(instance.DirectoryPath, "instance.json");
        var temporary = destination + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(instance, JsonDefaults.Options));
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
