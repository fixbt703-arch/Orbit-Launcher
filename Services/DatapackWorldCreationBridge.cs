using System.Diagnostics;
using System.IO.Compression;

namespace OrbitLauncher.Services;

/// <summary>
/// Vanilla Minecraft keeps the data-pack picker in a temporary mcworld-* directory,
/// not in the instance directory. Supply the instance's downloaded packs when that
/// picker is created, while leaving the downloaded originals untouched.
/// </summary>
public sealed class DatapackWorldCreationBridge : IDisposable
{
    private readonly string _sourceDirectory;
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;
    public string TemporaryDirectory { get; }

    public DatapackWorldCreationBridge(string instanceDirectory)
    {
        _sourceDirectory = Path.Combine(instanceDirectory, ".orbit", "downloads", "datapacks");
        TemporaryDirectory = Path.GetFullPath(Path.Combine(instanceDirectory, ".orbit", "temp", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(TemporaryDirectory);
        _watcher = new FileSystemWatcher(TemporaryDirectory, "mcworld-*")
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false
        };
        _watcher.Created += OnWorldPickerCreated;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnWorldPickerCreated(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !Directory.Exists(e.FullPath))
            return;

        _ = Task.Run(async () =>
        {
            // Minecraft creates the folder before its picker finishes initialisation.
            // Retrying also covers a transient file lock or a pack still downloading.
            for (var attempt = 0; attempt < 3 && !_disposed; attempt++)
            {
                try
                {
                    if (CopyPacks(e.FullPath))
                        return;
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Data-pack picker copy retry: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"Data-pack picker inaccessible: {ex.Message}");
                    return;
                }
                await Task.Delay(150 * (attempt + 1));
            }
        });
    }

    private bool CopyPacks(string destinationDirectory)
    {
        if (!Directory.Exists(_sourceDirectory) || !Directory.Exists(destinationDirectory))
            return true;

        var complete = true;
        foreach (var source in Directory.EnumerateFiles(_sourceDirectory, "*.zip", SearchOption.TopDirectoryOnly))
        {
            try
            {
                // Other ZIP downloads must not appear as broken data packs.
                using var archive = ZipFile.OpenRead(source);
                if (!archive.Entries.Any(entry => entry.FullName.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
                if (!File.Exists(destination))
                    File.Copy(source, destination);
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"Invalid data pack {source}: {ex.Message}");
            }
            catch (IOException ex)
            {
                complete = false;
                Debug.WriteLine($"Could not copy data pack {source}: {ex.Message}");
            }
        }
        return complete;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnWorldPickerCreated;
        _watcher.Dispose();
        // Only this launch's generated directory is removed; other running games own separate roots.
        try { if (Directory.Exists(TemporaryDirectory)) Directory.Delete(TemporaryDirectory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
