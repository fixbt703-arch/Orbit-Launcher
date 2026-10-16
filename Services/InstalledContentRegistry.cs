using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

/// <summary>
/// Persists catalog installations per instance. The registry stores only relative
/// paths, so it remains valid when an instance folder is renamed or migrated.
/// </summary>
public sealed class InstalledContentRegistry
{
    private const string RegistryFileName = "installed-content.json";

    public IReadOnlyList<InstalledContentRecord> Load(GameInstance instance)
    {
        var path = RegistryPath(instance);
        if (!File.Exists(path) && !File.Exists(path + ".bak"))
            return [];

        try
        {
            return AtomicJsonFile.Read<List<InstalledContentRecord>>(path) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public bool FileStillExists(GameInstance instance, InstalledContentRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.RelativePath))
            return false;

        try
        {
            var instanceRoot = Path.GetFullPath(instance.DirectoryPath)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(instance.DirectoryPath, record.RelativePath));
            if (!path.StartsWith(instanceRoot, StringComparison.OrdinalIgnoreCase))
                return false;
            if (record.SourceId == "modrinth" && record.ProjectType == "modpack" && record.State == "installed")
                return MrpackInstaller.IsInstalled(instance, path);
            if (File.Exists(path))
                return true;
            return path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(path + ".disabled");
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkAsync(
        GameInstance instance,
        ModSearchResult project,
        CatalogInstallState state,
        string destinationPath)
    {
        var root = Path.GetFullPath(instance.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(destinationPath).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        var relativePath = Path.GetRelativePath(instance.DirectoryPath, destinationPath);
        if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
            return;

        await AtomicJsonFile.UpdateAsync<List<InstalledContentRecord>>(RegistryPath(instance), saved =>
        {
            var records = saved ?? [];
            records.RemoveAll(record => string.Equals(record.SourceId, project.SourceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.ProjectId, project.ProjectId, StringComparison.OrdinalIgnoreCase));
            records.Add(new InstalledContentRecord
            {
                SourceId = project.SourceId,
                ProjectId = project.ProjectId,
                Title = project.Title,
                IconUrl = project.IconUrl,
                ProjectType = project.ProjectType,
                RelativePath = relativePath,
                State = state == CatalogInstallState.Downloaded ? "downloaded" : "installed",
                InstalledAt = DateTime.UtcNow
            });

            return records;
        });
    }

    private static string RegistryPath(GameInstance instance) =>
        Path.Combine(instance.DirectoryPath, ".orbit", RegistryFileName);
}

public sealed class InstalledContentRecord
{
    public string SourceId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string State { get; set; } = "installed";
    public DateTime InstalledAt { get; set; }
}
