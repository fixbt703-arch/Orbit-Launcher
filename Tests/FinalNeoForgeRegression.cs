using System.IO;
using System.Reflection;
using System.Text.Json;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class FinalNeoForgeRegression
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "orbit-neoforge-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var method = typeof(MinecraftService).GetMethod("FindNeoForgeProfile", BindingFlags.NonPublic | BindingFlags.Static)!;
            var instance = new GameInstance { GameVersion = "1.21.1", LoaderVersion = "21.1.1" };
            const string valid = "z-neoforge-21.1.1";
            var malformed = new[]
            {
                ("broken JSON", "{broken"),
                ("non-object root", "[]"),
                ("non-string id", "{\"id\":7}"),
                ("non-string inheritance", "{\"id\":\"a-neoforge-broken\",\"inheritsFrom\":7}"),
                ("non-array libraries", "{\"id\":\"a-neoforge-broken\",\"libraries\":{}}"),
                ("non-object library", "{\"id\":\"a-neoforge-broken\",\"libraries\":[7]}"),
                ("non-string library name", "{\"id\":\"a-neoforge-broken\",\"libraries\":[{\"name\":7}]}"),
                ("locked profile", "{}")
            };
            var failures = new List<Exception>();
            for (int index = 0; index < malformed.Length; index++)
            {
                var (label, contents) = malformed[index];
                var versions = Path.Combine(root, index.ToString());
                var damaged = Path.Combine(versions, "a-neoforge-broken");
                var correct = Path.Combine(versions, valid);
                Directory.CreateDirectory(damaged);
                Directory.CreateDirectory(correct);
                var damagedPath = Path.Combine(damaged, "a-neoforge-broken.json");
                var correctPath = Path.Combine(correct, valid + ".json");
                await File.WriteAllTextAsync(damagedPath, contents);
                await File.WriteAllTextAsync(correctPath, JsonSerializer.Serialize(new
                {
                    id = valid,
                    inheritsFrom = "1.21.1",
                    libraries = new[] { new { name = "net.neoforged:neoforge:21.1.1" } }
                }));
                var original = await File.ReadAllBytesAsync(damagedPath);
                var modified = File.GetLastWriteTimeUtc(damagedPath);
                try
                {
                    using var locked = label == "locked profile" ? File.Open(damagedPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                    var selected = (string?)method.Invoke(null, [instance, versions]);
                    if (selected != valid) throw new Exception("Exact profile not selected");
                }
                catch (Exception error)
                {
                    failures.Add(new Exception(label, error is TargetInvocationException { InnerException: { } inner } ? inner : error));
                }
                if (!File.Exists(damagedPath) || !original.SequenceEqual(await File.ReadAllBytesAsync(damagedPath))
                    || File.GetLastWriteTimeUtc(damagedPath) != modified)
                    failures.Add(new Exception(label + ": damaged profile changed"));
            }
            if (failures.Count != 0) throw new AggregateException("NeoForge lookup must skip unusable unrelated profiles without changing them", failures);
            Console.WriteLine("PASS final NeoForge: damaged, malformed and locked profiles do not block an exact profile; original files retained");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
