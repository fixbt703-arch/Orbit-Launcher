using System.IO;
using System.Text;
using OrbitLauncher.Services;

internal static class ZapretCompatibilitySmoke
{
    public static async Task<int> RunAsync(string root)
    {
        var count = 0;
        Directory.CreateDirectory(root);
        async Task Test(string name, Func<Task> action)
        {
            await action(); count++; Console.WriteLine("PASS Zapret compatibility: " + name);
        }
        string Fixture(string name, byte[]? bytes = null)
        {
            var path = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "lists"));
            Directory.CreateDirectory(Path.Combine(path, "bin"));
            File.WriteAllText(Path.Combine(path, "service.bat"), "fixture only");
            File.WriteAllText(Path.Combine(path, "bin", "winws.exe"), "fixture only");
            File.WriteAllBytes(Target(path), bytes ?? Encoding.UTF8.GetBytes("# user comment\r\ncustom.example\r\n"));
            return path;
        }
        string Storage(string path) => Path.Combine(root, "backups-" + Path.GetFileName(path));
        Task<ZapretCompatibilityResult> ApplyAsync(string path, CancellationToken token = default) => ZapretCompatibilityService.ApplyAsync(path, token, Storage(Path.TrimEndingDirectorySeparator(path)));
        Task<ZapretCompatibilityResult> RemoveAsync(string path, CancellationToken token = default) => ZapretCompatibilityService.RemoveAsync(path, token, Storage(Path.TrimEndingDirectorySeparator(path)));
        await Test("presence checks are read-only and follow apply, undo and legacy state", async () =>
        {
            Task<bool> Present(string path) => ZapretCompatibilityService.HasCompatibilityAsync(path, default, Storage(path));
            var path = Fixture("presence");
            Require(!await Present(path) && !Directory.Exists(Storage(path)), "presence check created state or adopted user exclusions");
            await ApplyAsync(path);
            Require(await Present(path), "installed compatibility not detected");
            await RemoveAsync(path);
            Require(!await Present(path), "leftover backups were mistaken for compatibility");
            var legacy = Path.Combine(path, ".orbit-launcher-backups"); Directory.CreateDirectory(legacy);
            var original = File.ReadAllBytes(Target(path));
            File.WriteAllBytes(Path.Combine(legacy, "list-exclude-user-20261025T120000123-0123456789abcdef0123456789abcdef.txt"), original);
            File.AppendAllText(Target(path), string.Join("\r\n", ZapretCompatibilityService.Domains) + "\r\n");
            var files = Directory.GetFiles(Storage(path));
            Require(await Present(path) && Directory.GetFiles(Storage(path)).Length == files.Length && Directory.GetFiles(legacy).Length == 1,
                "legacy inspection wrote receipts or migrated files");
            await RemoveAsync(path);
            Require(!await Present(path), "completed legacy receipt was mistaken for installed compatibility");
            var user = Fixture("presence-user", Encoding.UTF8.GetBytes(string.Join("\n", ZapretCompatibilityService.Domains) + "\n"));
            Require(!await Present(user), "user-owned exclusions were mistaken for Orbit compatibility");
        });
        await Test("preserves original bytes and comments, adds launcher domains, backs up outside Zapret", async () =>
        {
            var original = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("# комментарий\r\ncustom.example\r\nMINECRAFT.NET")).ToArray();
            var path = Fixture("preserve", original);
            var result = await ApplyAsync(path);
            Require(result.Changed && result.BackupPath != null, "repair was not saved with a backup");
            Require(File.ReadAllBytes(result.BackupPath!).SequenceEqual(original), "backup differs from original bytes");
            Require(!Path.GetFullPath(result.BackupPath!).StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "backup is inside Zapret");
            var output = File.ReadAllBytes(Target(path));
            Require(output.Take(original.Length).SequenceEqual(original), "existing text, BOM or comments changed");
            var lines = File.ReadAllLines(Target(path));
            foreach (var domain in new[] { "mojang.com", "minecraft.net", "minecraftservices.com", "fabricmc.net", "quiltmc.org", "minecraftforge.net", "neoforged.net", "modrinth.com", "curseforge.com", "forgecdn.net", "ely.by", "github.com", "githubusercontent.com", "githubassets.com", "microsoftonline.com", "microsoft.com", "live.com", "xboxlive.com", "msauth.net", "msftauth.net" })
                Require(lines.Count(line => line.Equals(domain, StringComparison.OrdinalIgnoreCase)) == 1, "missing or duplicated domain: " + domain);
            Require(!Directory.EnumerateFiles(Path.Combine(path, "lists")).Any(file => file != Target(path)), "temporary or backup file left in lists");
        });
        await Test("second application is byte-identical and adds no backup", async () =>
        {
            var path = Fixture("idempotent");
            var first = await ApplyAsync(path);
            var saved = File.ReadAllBytes(Target(path));
            var backups = Directory.GetFiles(Storage(path));
            var second = await ApplyAsync(path + Path.DirectorySeparatorChar);
            Require(first.Changed && !second.Changed && second.BackupPath == null, "second repair was not a no-op");
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(saved) && Directory.GetFiles(Storage(path)).Length == backups.Length, "no-op wrote files");
        });
        await Test("missing installation markers cannot create or alter exclusions", async () =>
        {
            foreach (var marker in new[] { "service.bat", Path.Combine("bin", "winws.exe"), Path.Combine("lists", "list-exclude-user.txt") })
            {
                var path = Fixture("invalid"); var original = File.ReadAllBytes(Target(path));
                File.Delete(Path.Combine(path, marker));
                await RejectAsync<IOException>(() => ApplyAsync(path));
                Require(!Directory.Exists(Path.Combine(path, ".orbit-launcher-backups")), "invalid folder got a backup");
                if (File.Exists(Target(path))) Require(File.ReadAllBytes(Target(path)).SequenceEqual(original), "invalid folder changed");
            }
        });
        await Test("oversized exclusions are rejected unchanged", async () =>
        {
            var original = Enumerable.Repeat((byte)'a', 1024 * 1024 + 1).ToArray();
            var path = Fixture("oversized", original);
            await RejectAsync<InvalidDataException>(() => ApplyAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original), "oversized list changed");
        });
        await Test("invalid UTF-8 is rejected unchanged", async () =>
        {
            var original = new byte[] { 0x23, 0x0a, 0xc3, 0x28 };
            var path = Fixture("invalid-utf8", original);
            await RejectAsync<DecoderFallbackException>(() => ApplyAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original), "invalid encoding changed");
        });
        await Test("cancelled operation never writes", async () =>
        {
            var path = Fixture("cancelled"); var original = File.ReadAllBytes(Target(path));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await RejectAsync<OperationCanceledException>(() => ApplyAsync(path, cancel.Token));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original) && !Directory.Exists(Path.Combine(path, ".orbit-launcher-backups")), "cancelled repair wrote files");
        });
        await Test("same-root concurrent callers perform one repair", async () =>
        {
            var path = Fixture("concurrent"); var original = File.ReadAllBytes(Target(path));
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ApplyAsync(path)));
            Require(results.Count(result => result.Changed) == 1 && Directory.GetFiles(Storage(path), "original-*.txt").Length == 1, "concurrent repairs overwrote each other");
            Require(File.ReadAllBytes(results.Single(result => result.Changed).BackupPath!).SequenceEqual(original), "concurrent backup lost original");
        });
        await Test("external atomic save is detected before commit and its new bytes survive", async () =>
        {
            var path = Fixture("external-save"); var original = File.ReadAllBytes(Target(path));
            var edited = Enumerable.Repeat((byte)'x', original.Length).ToArray();
            var temporary = Path.Combine(path, "editor-save.tmp");
            File.WriteAllBytes(temporary, edited); File.Move(temporary, Target(path), overwrite: true);
            var guard = typeof(ZapretCompatibilityService).GetMethod("EnsureUnchangedAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            await RejectAsync<IOException>(() => (Task)guard.Invoke(null, new object[] { Target(path), original, CancellationToken.None })!);
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(edited), "external atomic edit was lost");
            Require(!Directory.Exists(Path.Combine(path, ".orbit-launcher-backups")), "conflict wrote a backup");
        });
        await Test("locked target remains intact after failed replacement", async () =>
        {
            var path = Fixture("locked"); var original = File.ReadAllBytes(Target(path));
            using (var locked = new FileStream(Target(path), FileMode.Open, FileAccess.Read, FileShare.Read))
                await RejectAsync<IOException>(() => ApplyAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original), "failed replacement damaged original");
            Require(Directory.GetFiles(Storage(path), "original-*.txt").Length == 1 && Directory.GetFiles(Storage(path), "applied-*.txt").Length == 1, "replacement started before durable ownership proof");
            Require(Directory.GetFiles(Path.Combine(path, "lists")).Length == 1, "failed replacement left temporary files");
            Require((await ApplyAsync(path)).Changed, "failed repair could not be retried");
            Require((await RemoveAsync(path)).Changed && File.ReadAllBytes(Target(path)).SequenceEqual(original), "retry lost original bytes");
        });
        await Test("remove restores exact BOM and missing final newline; repeated remove and reapply are safe", async () =>
        {
            var original = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("# user\r\nMINECRAFT.NET")).ToArray();
            var path = Fixture("undo", original);
            await ApplyAsync(path);
            Require((await RemoveAsync(path)).Changed && File.ReadAllBytes(Target(path)).SequenceEqual(original), "remove did not restore original bytes");
            Require(!(await RemoveAsync(path)).Changed, "second remove changed user data");
            Require((await ApplyAsync(path)).Changed && (await RemoveAsync(path)).Changed, "reapply after removal failed");
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original), "reapply/remove lost original");
        });
        await Test("remove preserves later comments, edits and preexisting canonical domains", async () =>
        {
            var path = Fixture("edited", Encoding.UTF8.GetBytes("# before\nminecraft.net\ncustom.example\n"));
            await ApplyAsync(path);
            var current = File.ReadAllText(Target(path)).Replace("custom.example", "edited.example") + "# added later\nother.example\n";
            File.WriteAllText(Target(path), current, new UTF8Encoding(false));
            Require((await RemoveAsync(path)).Changed, "edited list was not removable");
            Require(File.ReadAllText(Target(path)) == "# before\nminecraft.net\nedited.example\n# added later\nother.example\n", "later edits or preexisting domains were lost");
        });
        await Test("missing state uses only intact managed comments; changed blocks cannot delete user domains", async () =>
        {
            var path = Fixture("missing-state");
            await ApplyAsync(path);
            File.AppendAllText(Target(path), "# later\nmojang.com\n");
            foreach (var file in Directory.GetFiles(Storage(path))) File.Delete(file);
            Require((await RemoveAsync(path)).Changed, "intact managed block could not be removed without state");
            Require(File.ReadAllText(Target(path)) == "# user comment\r\ncustom.example\r\n# later\nmojang.com\n", "missing-state fallback removed user data");
            var changed = Fixture("changed-block");
            await ApplyAsync(changed);
            File.AppendAllText(Target(changed), "# unrelated\n");
            File.WriteAllText(Target(changed), File.ReadAllText(Target(changed)).Replace("mojang.com", "user-change.example"));
            var edited = File.ReadAllBytes(Target(changed));
            await RejectAsync<ZapretCompatibilityOwnershipException>(() => RemoveAsync(changed));
            Require(File.ReadAllBytes(Target(changed)).SequenceEqual(edited), "modified managed block was removed");
        });
        await Test("user-only canonical domains are preserved without ownership state", async () =>
        {
            var original = Encoding.UTF8.GetBytes("# user exclusions\nminecraft.net\nmojang.com\n");
            var path = Fixture("user-only", original);
            Require(!(await RemoveAsync(path)).Changed && File.ReadAllBytes(Target(path)).SequenceEqual(original), "unowned domains were removed");
        });
        await Test("paired proof refuses canonical payload edits and user domains moved into the managed block", async () =>
        {
            foreach (var edit in new[] { "subset", "reordered", "user-domain" })
            {
                var path = Fixture("known-block-" + edit, Encoding.UTF8.GetBytes("# user\nminecraft.net\n"));
                await ApplyAsync(path);
                var installed = File.ReadAllText(Target(path));
                var edited = edit switch
                {
                    "subset" => installed.Replace("mojang.com\n", ""),
                    "reordered" => installed.Replace("mojang.com\nminecraftservices.com\n", "minecraftservices.com\nmojang.com\n"),
                    _ => installed.Replace("minecraft.net\n", "").Replace("# OrbitLauncher Zapret compatibility END ", "minecraft.net\n# OrbitLauncher Zapret compatibility END ")
                };
                Require(edited != installed, "fixture did not edit managed payload");
                File.WriteAllText(Target(path), edited, new UTF8Encoding(false));
                var bytes = File.ReadAllBytes(Target(path));
                await RejectAsync<ZapretCompatibilityOwnershipException>(() => RemoveAsync(path));
                Require(File.ReadAllBytes(Target(path)).SequenceEqual(bytes), "paired proof allowed changed canonical payload to be removed");
            }
        });
        await Test("legacy append migrates backups, preserves unknown files and removes only proven suffix", async () =>
        {
            var original = Encoding.UTF8.GetBytes("# old user\nminecraft.net\n");
            var path = Fixture("legacy", original);
            var legacy = Path.Combine(path, ".orbit-launcher-backups"); Directory.CreateDirectory(legacy);
            var name = "list-exclude-user-20261025T120000123-0123456789abcdef0123456789abcdef.txt";
            File.WriteAllBytes(Path.Combine(legacy, name), original);
            File.WriteAllText(Path.Combine(legacy, "keep-me.txt"), "user file");
            var missing = ZapretCompatibilityService.Domains.Where(domain => domain != "minecraft.net");
            File.AppendAllText(Target(path), string.Join("\n", missing) + "\n# later\nother.example\n");
            Require((await RemoveAsync(path)).Changed, "legacy append was not removed");
            Require(File.ReadAllText(Target(path)) == "# old user\nminecraft.net\n# later\nother.example\n", "legacy remove lost user edits");
            Require(File.Exists(Path.Combine(Storage(path), name)) && !File.Exists(Path.Combine(legacy, name)), "recognized legacy backup was not migrated");
            Require(File.ReadAllText(Path.Combine(legacy, "keep-me.txt")) == "user file", "legacy cleanup deleted unknown file");
            Require(!(await RemoveAsync(path)).Changed, "legacy repeated removal changed data");
        });
        await Test("ambiguous legacy edits remain untouched and backups survive retry", async () =>
        {
            var original = Encoding.UTF8.GetBytes("# old user\n"); var path = Fixture("legacy-edited", original);
            var legacy = Path.Combine(path, ".orbit-launcher-backups"); Directory.CreateDirectory(legacy);
            File.WriteAllBytes(Path.Combine(legacy, "list-exclude-user-20261025T120000123-0123456789abcdef0123456789abcdef.txt"), original);
            File.WriteAllText(Target(path), "# edited user\n" + string.Join("\n", ZapretCompatibilityService.Domains) + "\n");
            var edited = File.ReadAllBytes(Target(path));
            await RejectAsync<ZapretCompatibilityOwnershipException>(() => RemoveAsync(path));
            await RejectAsync<ZapretCompatibilityOwnershipException>(() => RemoveAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(edited), "ambiguous legacy list changed");
            Require(Directory.GetFiles(Storage(path), "list-exclude-user-*.txt").Length == 1, "legacy proof was lost on retry");
        });
        await Test("legacy undo preserves later duplicate user domain lines across retries", async () =>
        {
            var original = Array.Empty<byte>(); var path = Fixture("legacy-duplicate", original);
            var legacy = Path.Combine(path, ".orbit-launcher-backups"); Directory.CreateDirectory(legacy);
            File.WriteAllBytes(Path.Combine(legacy, "list-exclude-user-20261025T120000123-0123456789abcdef0123456789abcdef.txt"), original);
            var block = string.Join(Environment.NewLine, ZapretCompatibilityService.Domains) + Environment.NewLine;
            File.WriteAllText(Target(path), block + block);
            Require((await RemoveAsync(path)).Changed && File.ReadAllText(Target(path)) == block, "legacy proof consumed later duplicate lines");
            Require(!(await RemoveAsync(path)).Changed && File.ReadAllText(Target(path)) == block, "legacy retry consumed later duplicate lines");
            File.AppendAllText(Target(path), block);
            Require(!(await RemoveAsync(path)).Changed && File.ReadAllText(Target(path)) == block + block, "completed legacy undo replayed over later user edits");
        });
        await Test("legacy undo retries failed replacement and recovers missing completion acknowledgement", async () =>
        {
            var original = Encoding.UTF8.GetBytes("# legacy user\n"); var path = Fixture("legacy-retry", original);
            var legacy = Path.Combine(path, ".orbit-launcher-backups"); Directory.CreateDirectory(legacy);
            File.WriteAllBytes(Path.Combine(legacy, "list-exclude-user-20261025T120000123-0123456789abcdef0123456789abcdef.txt"), original);
            File.AppendAllText(Target(path), string.Join("\n", ZapretCompatibilityService.Domains) + "\n");
            var installed = File.ReadAllBytes(Target(path));
            using (var locked = new FileStream(Target(path), FileMode.Open, FileAccess.Read, FileShare.Read))
                await RejectAsync<IOException>(() => RemoveAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(installed) && Directory.GetFiles(Storage(path), "legacy-*.done").Length == 0, "failed legacy replacement was acknowledged");
            Require((await RemoveAsync(path)).Changed && File.ReadAllBytes(Target(path)).SequenceEqual(original), "legacy retry did not recover receipt");
            var done = Directory.GetFiles(Storage(path), "legacy-*.done").Single(); File.Delete(done);
            Require(!(await RemoveAsync(path)).Changed && File.Exists(done), "post-replacement receipt was not acknowledged");
            Require(!(await RemoveAsync(path)).Changed, "completed legacy receipt was not idempotent");
        });
        await Test("backup paths inside Zapret and failed backup writes leave target intact", async () =>
        {
            var path = Fixture("bad-storage"); var original = File.ReadAllBytes(Target(path));
            await RejectAsync<InvalidDataException>(() => ZapretCompatibilityService.ApplyAsync(path, default, Path.Combine(path, "backup")));
            File.WriteAllText(Storage(path), "block directory creation");
            await RejectAsync<IOException>(() => ApplyAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original), "failed backup write changed target");
        });
        await Test("cancelled removal leaves installed compatibility intact", async () =>
        {
            var path = Fixture("cancel-remove"); await ApplyAsync(path); var installed = File.ReadAllBytes(Target(path));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await RejectAsync<OperationCanceledException>(() => RemoveAsync(path, cancel.Token));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(installed), "cancelled removal changed target");
        });
        await Test("preexisting complete domain list is never adopted or removed", async () =>
        {
            var original = Encoding.UTF8.GetBytes("# all user exclusions\n" + string.Join("\n", ZapretCompatibilityService.Domains) + "\n");
            var path = Fixture("complete-user-list", original);
            Require(!(await ApplyAsync(path)).Changed && !(await RemoveAsync(path)).Changed, "preexisting domains became launcher-owned");
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original) && !Directory.Exists(Storage(path)), "user-only list wrote state");
        });
        await Test("near-limit output is rejected before creating backups", async () =>
        {
            var original = Enumerable.Repeat((byte)'a', 1024 * 1024 - 10).ToArray(); var path = Fixture("output-limit", original);
            await RejectAsync<InvalidDataException>(() => ApplyAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(original) && !Directory.Exists(Storage(path)), "oversized replacement wrote data");
        });
        await Test("legacy exact undo restores original bytes and cleans only an empty legacy directory", async () =>
        {
            var original = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("# before\r\nminecraft.net")).ToArray();
            var path = Fixture("legacy-exact", original);
            var legacy = Path.Combine(path, ".orbit-launcher-backups"); Directory.CreateDirectory(legacy);
            File.WriteAllBytes(Path.Combine(legacy, "list-exclude-user-20261025T120000123-0123456789abcdef0123456789abcdef.txt"), original);
            File.AppendAllText(Target(path), "\r\n" + string.Join("\r\n", ZapretCompatibilityService.Domains.Where(domain => domain != "minecraft.net")) + "\r\n");
            Require((await RemoveAsync(path)).Changed && File.ReadAllBytes(Target(path)).SequenceEqual(original), "legacy exact undo changed original bytes");
            Require(!Directory.Exists(legacy), "empty legacy directory remained");
        });
        await Test("incomplete applied proof falls back to intact comments; corrupted proof cannot authorize removal", async () =>
        {
            var path = Fixture("broken-proof"); await ApplyAsync(path); var installed = File.ReadAllBytes(Target(path));
            var proof = Directory.GetFiles(Storage(path), "applied-*.txt").Single(); File.Delete(proof);
            Require((await RemoveAsync(path)).Changed, "missing applied copy blocked intact comment ownership");
            File.WriteAllBytes(Target(path), installed); File.WriteAllText(proof, "mojang.com\n");
            await RejectAsync<ZapretCompatibilityOwnershipException>(() => RemoveAsync(path));
            Require(File.ReadAllBytes(Target(path)).SequenceEqual(installed), "corrupt proof removed domains");
        });
        await Test("external backup directory and its ancestors reject junctions", async () =>
        {
            foreach (var ancestor in new[] { false, true })
            {
                var path = Fixture("storage-link"); var original = File.ReadAllBytes(Target(path));
                var external = Path.Combine(root, "storage-destination-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(external);
                var link = Storage(path); CreateJunction(link, external);
                var store = ancestor ? Path.Combine(link, "child") : link;
                await RejectAsync<InvalidDataException>(() => ZapretCompatibilityService.ApplyAsync(path, default, store));
                Require(File.ReadAllBytes(Target(path)).SequenceEqual(original) && !Directory.EnumerateFileSystemEntries(external).Any(), "storage junction was followed");
                Directory.Delete(link);
            }
        });
        await Test("reparse root, lists, target and backup directory are rejected", async () =>
        {
            foreach (var name in new[] { "root", "lists", "target", ".orbit-launcher-backups" })
            {
                var path = Fixture("reparse"); var original = File.ReadAllBytes(Target(path));
                var external = Path.Combine(root, "external-" + Guid.NewGuid().ToString("N"));
                string link;
                if (name == "root") { Directory.Move(path, external); link = path; }
                else if (name == "lists") { Directory.Move(Path.Combine(path, name), external); link = Path.Combine(path, name); }
                else if (name == "target")
                {
                    Directory.CreateDirectory(external);
                    File.Move(Target(path), Path.Combine(external, "list-exclude-user.txt")); link = Target(path);
                }
                else { Directory.CreateDirectory(external); link = Path.Combine(path, name); }
                CreateJunction(link, external);
                await RejectAsync<InvalidDataException>(() => ApplyAsync(path));
                var target = name == "root" ? Target(external) : name is "lists" or "target" ? Path.Combine(external, "list-exclude-user.txt") : Target(path);
                Require(File.ReadAllBytes(target).SequenceEqual(original), "reparse operation changed target");
                Directory.Delete(link);
            }
        });
        var linkRoot = Fixture("target-link");
        var linkedOriginal = File.ReadAllBytes(Target(linkRoot));
        var linkedExternal = Path.Combine(root, "external-file-" + Guid.NewGuid().ToString("N"));
        File.Move(Target(linkRoot), linkedExternal);
        var canCreateFileLink = true;
        try { File.CreateSymbolicLink(Target(linkRoot), linkedExternal); }
        catch (IOException exception) when ((exception.HResult & 0xffff) == 1314)
        {
            canCreateFileLink = false;
            Console.WriteLine("SKIP Zapret compatibility: file symlink requires Windows privilege (1314)");
        }
        if (canCreateFileLink) await Test("reparse target is rejected without touching its destination", async () =>
        {
            await RejectAsync<InvalidDataException>(() => ApplyAsync(linkRoot));
            Require(File.ReadAllBytes(linkedExternal).SequenceEqual(linkedOriginal), "linked file changed");
            File.Delete(Target(linkRoot));
        });
        return count;
    }

    private static string Target(string root) => Path.Combine(root, "lists", "list-exclude-user.txt");
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected rejection: " + typeof(T).Name);
    }
    private static void CreateJunction(string link, string destination)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = "/d /c mklink /J \"" + link + "\" \"" + destination + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        Require(process.ExitCode == 0, "could not create fixture junction: " + output);
    }
}

