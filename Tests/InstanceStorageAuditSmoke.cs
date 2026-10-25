using System.Diagnostics;
using System.IO;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

internal static class InstanceStorageAuditSmoke
{
    public static async Task<int> RunAsync(string parent)
    {
        var root = Path.Combine(parent, "instance-storage-" + Guid.NewGuid().ToString("N"));
        var instances = Path.Combine(root, "instances");
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(instances);
        Directory.CreateDirectory(external);
        var definition = Path.Combine(external, "instance.json");
        const string original = "{\"name\":\"External original\",\"gameVersion\":\"1.21.1\"}";
        File.WriteAllText(definition, original);
        var link = Path.Combine(instances, "Linked");
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + external + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception("Could not create storage junction fixture: " + await process.StandardError.ReadToEndAsync());
        }
        try
        {
            var rejected = false;
            try
            {
                await new InstanceRepository(instances, Path.Combine(root, "shared"))
                    .SaveAsync(new GameInstance { Name = "Linked", DirectoryPath = link });
            }
            catch (IOException) { rejected = true; }
            if (!rejected || File.ReadAllText(definition) != original || File.Exists(definition + ".bak"))
                throw new Exception("Saving an instance junction wrote metadata outside the instance root");
            Console.WriteLine("PASS instance save rejects junctions without changing external metadata");
        }
        finally
        {
            if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(link);
        }
        var repository = new InstanceRepository(instances, Path.Combine(root, "shared"));
        var instance = await repository.CreateAsync(new GameInstance { Name = "Original" });
        var stale = (await repository.GetAllAsync()).Single();
        instance.Name = "Renamed";
        await repository.SaveAsync(instance);
        var missingRejected = false;
        try { await repository.SaveAsync(stale); }
        catch (DirectoryNotFoundException) { missingRejected = true; }
        if (!missingRejected || Directory.Exists(stale.DirectoryPath)
            || (await repository.GetAllAsync()).Single().Id != instance.Id)
            throw new Exception("Saving stale metadata recreated an instance at its old path after rename");
        Console.WriteLine("PASS instance save rejects a stale path after rename without recreating duplicate metadata");
        return 2;
    }
}
