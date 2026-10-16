using System.IO.Compression;

namespace OrbitLauncher.Services;

public static class InstanceArchive
{
    public static IReadOnlyList<string> RegularFiles(string root) => Snapshot(root).Files;
    private static (List<string> Files, List<string> Directories) Snapshot(string root)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var pending = new Stack<string>(); pending.Push(root);
        var files = new List<string>();
        var directories = new List<string>();
        while (pending.Count > 0)
        {
            var directory = pending.Pop(); AssertRegular(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                AssertRegular(entry);
                if (Directory.Exists(entry)) { pending.Push(entry); directories.Add(entry); } else files.Add(entry);
            }
        }
        return (files, directories);
    }

    private static void AssertRegular(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Сборка содержит ссылку или junction. Копирование внешних файлов остановлено: " + path);
    }

    public static void Export(string source, string destination)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        destination = Path.GetFullPath(destination);
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Сохраните ZIP за пределами папки сборки.");
        var entries = Snapshot(source);
        var parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".orbit-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var directory in entries.Directories)
                    archive.CreateEntry(Path.GetRelativePath(source, directory).Replace('\\', '/') + "/");
                foreach (var file in entries.Files)
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(source, file).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Copy(string source, string destination, bool skipDefinition)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нельзя копировать сборку внутрь самой себя.");
        var entries = Snapshot(source);
        Directory.CreateDirectory(destination);
        foreach (var directory in entries.Directories) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in entries.Files)
        {
            var relative = Path.GetRelativePath(source, file);
            if (skipDefinition && (relative.Equals("instance.json", StringComparison.OrdinalIgnoreCase)
                || (!relative.Contains(Path.DirectorySeparatorChar) && relative.StartsWith("instance.json.", StringComparison.OrdinalIgnoreCase)))) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
