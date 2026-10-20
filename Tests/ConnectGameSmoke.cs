using System.Diagnostics;
using System.IO;
using System.Reflection;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Services.Connect;
using OrbitLauncher.Utilities;

internal static class ConnectGameSmoke
{
    public static async Task RunAsync(string directory, string mod)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory)) throw new IOException("Use a new isolated game test directory");
        Directory.CreateDirectory(directory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var token = deadline.Token;
        var shared = new MinecraftPath(AppPaths.Minecraft);
        using var network = LauncherNetwork.CreateClient();
        var launcher = new MinecraftLauncher(MinecraftLauncherParameters.CreateDefault(shared, network));
        var workers = new List<ConnectWorkerClient>();
        var games = new List<(Process Process, string Directory, Task<string> Out, Task<string> Err)>();
        async Task<ConnectWorkerClient> Launch(string mode, string username)
        {
            string folder = Path.Combine(directory, mode); Directory.CreateDirectory(Path.Combine(folder, "mods"));
            File.Copy(mod, Path.Combine(folder, "mods", "orbit-connect-live-test.jar"));
            await File.WriteAllTextAsync(Path.Combine(folder, "options.txt"), "onboarded:true\nskipMultiplayerWarning:true\nfullscreen:false\noverrideWidth:854\noverrideHeight:480\nrenderDistance:4\nsimulationDistance:4\nmaxFps:45\npauseOnLostFocus:false\n", token);
            var instance = new GameInstance { Name = "Orbit live " + mode, GameVersion = "26.2", Loader = "Fabric", OrbitConnectEnabled = true, DirectoryPath = folder };
            var arguments = await ConnectGameIntegration.ArgumentsAsync(instance, token);
            var worker = await ConnectWorkerClient.ForAsync(instance, token); workers.Add(worker);
            var path = new MinecraftPath(folder) { Library = shared.Library, Versions = shared.Versions, Resource = shared.Resource, Assets = shared.Assets, Runtime = shared.Runtime };
            var options = new MLaunchOption
            {
                Session = MSession.CreateOfflineSession(username), Path = path, MinimumRamMb = 512, MaximumRamMb = 1536,
                JavaPath = Path.Combine(shared.Runtime, "windows-x64", "java-runtime-epsilon", "bin", "java.exe"),
                GameLauncherName = "OrbitConnectIsolatedTest", GameLauncherVersion = "1.0",
                ExtraJvmArguments = arguments.Append(new MArgument("-Dorbit.connect.smoke.mode=" + mode)).ToArray()
            };
            var game = await launcher.BuildProcessAsync("fabric-loader-0.19.5-26.2", options);
            game.StartInfo.UseShellExecute = false; game.StartInfo.CreateNoWindow = true; game.StartInfo.WorkingDirectory = folder;
            game.StartInfo.RedirectStandardOutput = game.StartInfo.RedirectStandardError = true;
            game.Start(); games.Add((game, folder, game.StandardOutput.ReadToEndAsync(), game.StandardError.ReadToEndAsync()));
            await worker.RequestAsync("attach", new { pid = game.Id }, token);
            return worker;
        }
        void CheckGames()
        {
            foreach (var game in games)
            {
                if (File.Exists(Path.Combine(game.Directory, "connect-failed.txt"))) throw new Exception(File.ReadAllText(Path.Combine(game.Directory, "connect-failed.txt")));
                if (game.Process.HasExited) throw new Exception("Minecraft exited early: " + game.Process.ExitCode + "; " + game.Directory);
            }
        }
        try
        {
            var host = await Launch("host", "OrbitHostTest");
            ConnectStatus status;
            do
            {
                CheckGames(); await Task.Delay(500, token); status = await host.RequestAsync("state", token: token);
                if (status.Phase == "error") throw new Exception("Live world host: " + status.Message);
            } while (status.Phase != "hosting");
            Console.WriteLine("PASS live Minecraft 26.2 / Fabric 0.19.5: isolated world, real LAN socket automatically published by agent");
            var chatDeadline = DateTime.UtcNow.AddSeconds(12);
            while (!File.Exists(Path.Combine(directory, "host", "connect-chat.ok")))
            { CheckGames(); if (DateTime.UtcNow > chatDeadline) throw new Exception("Clickable Orbit invitation missing from real Minecraft chat"); await Task.Delay(250, token); }
            Console.WriteLine("PASS live game UI: real clickable copy invitation in Minecraft chat");
            var guest = await Launch("guest", "OrbitGuestTest");
            await File.WriteAllTextAsync(Path.Combine(directory, "guest", "connect-invitation"), status.Invitation, token);
            while (!File.Exists(Path.Combine(directory, "guest", "connect-joined.ok"))) { CheckGames(); await Task.Delay(500, token); }
            var connected = await guest.RequestAsync("state", token: token);
            if (connected.Phase != "connected") throw new Exception("Guest entered world without connected controller");
            await Task.Delay(5000, token); CheckGames();
            Console.WriteLine("PASS live Minecraft host + guest: invite pasted through real ServerAddress hook, guest entered host world, measured route " + connected.PingMs?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms; no Orbit Client or Fabric API required");
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS two real Minecraft 26.2 Fabric 0.19.5 clients, Java25, fresh isolated world, offline local fixtures, host LAN -> auto invite -> guest world entered. Same machine, not two ISPs.\n", token);
        }
        finally
        {
            foreach (var game in games) await File.WriteAllTextAsync(Path.Combine(game.Directory, "connect-stop"), "stop");
            foreach (var game in games)
            {
                try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await game.Process.WaitForExitAsync(stop.Token); }
                catch { if (!game.Process.HasExited) { game.Process.Kill(true); await game.Process.WaitForExitAsync(); } }
                await File.WriteAllTextAsync(Path.Combine(game.Directory, "stdout.log"), await game.Out);
                await File.WriteAllTextAsync(Path.Combine(game.Directory, "stderr.log"), await game.Err);
                File.Delete(Path.Combine(game.Directory, "connect-invitation")); game.Process.Dispose();
            }
            foreach (var worker in workers)
            {
                var process = (Process)typeof(ConnectWorkerClient).GetField("_worker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(worker)!;
                try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await process.WaitForExitAsync(stop.Token); }
                catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            }
        }
    }
}
