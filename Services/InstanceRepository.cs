using System.IO.Compression;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed class InstanceRepository
{
    private const string DefinitionFile = "instance.json";

    public async Task<IReadOnlyList<GameInstance>> GetAllAsync()
    {
        AppPaths.EnsureCreated();
        var instances = new List<GameInstance>();

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.Instances))
        {
            var file = Path.Combine(directory, DefinitionFile);
            if (!File.Exists(file))
                continue;

            try
            {
                await using var stream = File.OpenRead(file);
                var instance = await JsonSerializer.DeserializeAsync<GameInstance>(stream, JsonDefaults.Options);
                if (instance is null)
                    continue;

                instance.DirectoryPath = directory;
                Directory.CreateDirectory(instance.ModsPath);
                instances.Add(instance);
            }
            catch
            {
                // A damaged instance is skipped so one bad file cannot block the launcher.
            }
        }

        return instances
            .OrderByDescending(x => x.LastPlayedAt ?? x.CreatedAt)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<GameInstance> CreateAsync(GameInstance instance)
    {
        AppPaths.EnsureCreated();
        instance.Id = Guid.NewGuid().ToString("N");
        instance.DirectoryPath = Path.Combine(AppPaths.Instances, instance.Id);
        Directory.CreateDirectory(instance.DirectoryPath);
        Directory.CreateDirectory(instance.ModsPath);
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, ".orbit"));
        await SaveAsync(instance);
        return instance;
    }

    public async Task SaveAsync(GameInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.DirectoryPath))
            throw new InvalidOperationException("У сборки не указан каталог.");

        Directory.CreateDirectory(instance.DirectoryPath);
        var destination = Path.Combine(instance.DirectoryPath, DefinitionFile);
        var temporary = destination + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, instance, JsonDefaults.Options);

        File.Move(temporary, destination, true);
    }

    public async Task DeleteAsync(GameInstance instance)
    {
        if (!Directory.Exists(instance.DirectoryPath))
            return;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                await Task.Run(() => Directory.Delete(instance.DirectoryPath, true));
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(150 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(150 * attempt);
            }
        }
    }

    public async Task<GameInstance> DuplicateAsync(GameInstance source)
    {
        var clone = source.CloneDefinition();
        clone.Name = source.Name + " — копия";
        await CreateAsync(clone);

        CopyDirectory(source.DirectoryPath, clone.DirectoryPath, skipDefinition: true);
        clone.IconFile = source.IconFile;
        await SaveAsync(clone);
        return clone;
    }

    public Task ExportAsync(GameInstance instance, string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        ZipFile.CreateFromDirectory(instance.DirectoryPath, zipPath, CompressionLevel.Optimal, false);
        return Task.CompletedTask;
    }

    public async Task<GameInstance> ImportAsync(string zipPath)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "OrbitLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temporary);
            var definition = Directory.EnumerateFiles(temporary, DefinitionFile, SearchOption.AllDirectories).FirstOrDefault();
            GameInstance instance;
            string contentRoot;

            if (definition is not null)
            {
                await using var stream = File.OpenRead(definition);
                instance = await JsonSerializer.DeserializeAsync<GameInstance>(stream, JsonDefaults.Options)
                           ?? new GameInstance();
                contentRoot = Path.GetDirectoryName(definition)!;
            }
            else
            {
                instance = new GameInstance { Name = Path.GetFileNameWithoutExtension(zipPath) };
                contentRoot = temporary;
            }

            var imported = await CreateAsync(instance.CloneDefinition());
            imported.Name = instance.Name + " — импорт";
            CopyDirectory(contentRoot, imported.DirectoryPath, skipDefinition: true);
            imported.IconFile = instance.IconFile;
            await SaveAsync(imported);
            return imported;
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);
        }
    }

    private static void CopyDirectory(string source, string destination, bool skipDefinition)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (skipDefinition && string.Equals(Path.GetFileName(file), DefinitionFile, StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), false);
    }
}
