using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using OrbitLauncher.Services;

internal static class FinalDatapackRegression
{
    public static async Task RunAsync()
    {
        var instance = Path.Combine(Path.GetTempPath(), "OrbitFinalDatapack-" + Guid.NewGuid().ToString("N"));
        try
        {
            var downloads = Path.Combine(instance, ".orbit", "downloads", "datapacks");
            Directory.CreateDirectory(downloads);
            foreach (var name in new[] { "first.zip", "second.zip" })
            {
                using var zip = ZipFile.Open(Path.Combine(downloads, name), ZipArchiveMode.Create);
                using var writer = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open());
                writer.Write("{\"pack\":{\"pack_format\":107,\"description\":\"" + name + "\"}}");
            }
            // The later healthy copy signals that the pass has encountered the locked pack.
            var sources = Directory.EnumerateFiles(downloads, "*.zip").ToArray();
            var locked = sources[0];
            var healthy = sources[1];
            var lockedBytes = File.ReadAllBytes(locked);
            var healthyBytes = File.ReadAllBytes(healthy);
            var invalid = Path.Combine(downloads, "invalid.zip");
            File.WriteAllText(invalid, "This is not a ZIP archive.");

            string temporaryDirectory;
            using (var bridge = new DatapackWorldCreationBridge(instance))
            {
                temporaryDirectory = bridge.TemporaryDirectory;
                var picker = Path.Combine(temporaryDirectory, "mcworld-regression");
                var lockedDestination = Path.Combine(picker, Path.GetFileName(locked));
                var healthyDestination = Path.Combine(picker, Path.GetFileName(healthy));
                using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Directory.CreateDirectory(picker);
                    await UntilAsync(() => HasBytes(healthyDestination, healthyBytes),
                        "A locked pack prevented the other valid pack from reaching the picker.");
                    Require(!File.Exists(lockedDestination), "Exclusively locked pack was unexpectedly copied.");
                }
                await UntilAsync(() => HasBytes(lockedDestination, lockedBytes),
                    "Data pack was never retried after its temporary file lock was released.");
                Require(HasBytes(healthyDestination, healthyBytes), "Retry changed the healthy pack.");
                Require(!File.Exists(Path.Combine(picker, "invalid.zip")), "Invalid ZIP entered the picker.");
                Require(HasBytes(locked, lockedBytes) && HasBytes(healthy, healthyBytes),
                    "Copying data packs changed or removed the downloaded originals.");
                Require(File.ReadAllText(invalid) == "This is not a ZIP archive.", "Invalid original was changed.");
            }
            Require(!Directory.Exists(temporaryDirectory), "Bridge did not clean up its own temporary directory.");
            Require(HasBytes(locked, lockedBytes) && HasBytes(healthy, healthyBytes),
                "Bridge cleanup removed the downloaded originals.");
        }
        finally
        {
            if (Directory.Exists(instance)) Directory.Delete(instance, true);
        }
    }

    private static bool HasBytes(string path, byte[] expected)
    {
        try { return File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected); }
        catch (IOException) { return false; }
    }

    private static async Task UntilAsync(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(4)) throw new Exception(failure);
            await Task.Delay(10);
        }
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new Exception(failure);
    }
}
