using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed partial class ModCompatibilityRepairService
{
    private async Task<RepairState> RepairCurseForgeFileAsync(GameInstance instance, string file,
        InstalledContentRecord record, string backups, IProgress<RepairUpdate> progress, int completed, int total,
        CancellationToken token)
    {
        progress.Report(new(completed, total, Path.GetFileName(file), L(
            "Ищем совместимый файл в CurseForge…", "Finding a compatible file on CurseForge…"), RepairState.Checking));
        var versions = await _curseForge.GetProjectVersionsAsync(record.ProjectId, instance.GameVersion, instance.Loader, token);
        var compatible = versions.Where(version => version.GameVersions.Contains(instance.GameVersion, StringComparer.OrdinalIgnoreCase)
            && (version.Loaders.Count == 0 || version.Loaders.Contains(instance.Loader, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(version => ChannelRank(version.Channel)).ThenByDescending(version => version.PublishedAt).ToList();
        if (compatible.Count == 0)
        {
            progress.Report(new(completed, total, Path.GetFileName(file), L(
                "В CurseForge нет совместимого выпуска; файл оставлен без изменений.",
                "CurseForge has no compatible release; the file was left unchanged."), RepairState.NeedsAttention));
            return RepairState.NeedsAttention;
        }
        if (compatible.Any(version => version.FileName.Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)))
        {
            progress.Report(new(completed, total, Path.GetFileName(file), L(
                "Версия игры и загрузчик совместимы по данным CurseForge.",
                "Game and loader versions are compatible according to CurseForge."), RepairState.Compatible));
            return RepairState.Compatible;
        }
        var chosen = compatible.FirstOrDefault(version => int.TryParse(version.Id, out _) &&
            SafeJarName(version.FileName).Length > 0);
        if (chosen is null || !int.TryParse(chosen.Id, out var fileId))
        {
            progress.Report(new(completed, total, Path.GetFileName(file), L(
                "Для совместимого выпуска нет доступного JAR-файла.",
                "No available JAR file for a compatible release."), RepairState.NeedsAttention));
            return RepairState.NeedsAttention;
        }
        var destination = Path.Combine(instance.ModsPath, chosen.FileName);
        if (File.Exists(destination))
        {
            progress.Report(new(completed, total, Path.GetFileName(file), L(
                "Совместимый JAR уже есть в папке. Проверьте дубликаты.",
                "The compatible JAR is already present. Review duplicates."), RepairState.NeedsAttention));
            return RepairState.NeedsAttention;
        }
        var staging = Path.Combine(instance.DirectoryPath, ".orbit", "repair-staging", Guid.NewGuid().ToString("N"));
        try
        {
            var downloaded = await _curseForge.DownloadFileForRepairAsync(record.ProjectId, fileId, staging, token);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(backups);
            var backup = UniquePath(backups, Path.GetFileName(file));
            File.Move(file, backup);
            try { File.Move(downloaded, destination); }
            catch { File.Move(backup, file); throw; }
            progress.Report(new(completed, total, Path.GetFileName(file), LocalizationService.IsEnglish
                ? $"Replaced with {chosen.FileName}. The previous file was backed up."
                : $"Заменён на {chosen.FileName}. Старый файл сохранён в резервной копии.", RepairState.Replaced));
            try { await UpdateRegistryAsync(instance, record.ProjectId, destination, "curseforge"); }
            catch { /* The verified file is installed; its catalog record can be rebuilt later. */ }
            return RepairState.Replaced;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (IOException) { /* Keep the installed mod even if a temporary file is locked. */ }
            catch (UnauthorizedAccessException) { /* A later cleanup can remove the staging directory. */ }
        }
    }
}
