using System.Diagnostics;
using System.IO;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

/// <summary>Full cached Fabric launch with a fictional session in a new, isolated directory.</summary>
internal static class SkinLaunchSmoke
{
    public static async Task RunAsync(string directory, string agent, string installedMods, bool refill = false, string? scenario = null)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory)) throw new IOException("Smoke directory must not already exist.");
        Directory.CreateDirectory(Path.Combine(directory, "mods"));
        using var network = LauncherNetwork.CreateClient();
        if (installedMods == "--bundled")
            await new OrbitClientService(network).EnsureInstalledAsync(new OrbitLauncher.Models.GameInstance
            { DirectoryPath = directory, GameVersion = "26.2", Loader = "Fabric", LoaderVersion = "0.19.5", OrbitClientEnabled = true });
        else
            // Copy code only, never the real account, configuration, worlds or server list.
            foreach (var mod in Directory.EnumerateFiles(installedMods, "*.jar"))
                File.Copy(mod, Path.Combine(directory, "mods", Path.GetFileName(mod)));
        var scaleRegression = scenario == "ui-scale";
        await File.WriteAllTextAsync(Path.Combine(directory, "options.txt"), $"onboarded:true\nskipMultiplayerWarning:true\nfullscreen:false\noverrideWidth:{(scaleRegression ? 1920 : 1600)}\noverrideHeight:{(scaleRegression ? 1080 : 900)}\nguiScale:{(scaleRegression ? 4 : 2)}\nlang:ru_ru\nrenderDistance:6\n");
        var shared = new MinecraftPath(AppPaths.Minecraft);
        var path = new MinecraftPath(directory) {
            Library = shared.Library, Versions = shared.Versions, Resource = shared.Resource,
            Assets = shared.Assets, Runtime = shared.Runtime
        };
        var launcher = new MinecraftLauncher(MinecraftLauncherParameters.CreateDefault(shared, network));
        var ownSkin = scenario is "skin-offline" or "skin-ely";
        var session = MSession.CreateOfflineSession(ownSkin ? "ErickSkrauch" : "OrbitViewer");
        var authArguments = scenario == "skin-ely"
            ? new[] { ElyGameIntegration.AgentArgument(await ElyGameIntegration.EnsureAgentAsync(network, default, Path.Combine(directory, "ely-agent"))) }
            : Array.Empty<MArgument>();
        var options = new MLaunchOption {
            Session = session, Path = path, MinimumRamMb = 512, MaximumRamMb = 3072,
            JavaPath = Path.Combine(shared.Runtime, "windows-x64", "java-runtime-epsilon", "bin", "java.exe"),
            GameLauncherName = "OrbitSmoke", GameLauncherVersion = "1.4.5",
            ExtraJvmArguments = authArguments.Concat(LicensedSkinBridge.Arguments(agent, session.UUID!, session.Username!,includeViewer:ownSkin))
                .Concat(new[] { new MArgument("-Dorbit.client.smoke-test=true") })
                .Concat(refill ? new[] { new MArgument("-Dorbit.client.world-smoke=true"), new MArgument("-Dorbit.client.refill-smoke=true") } : Array.Empty<MArgument>())
                .Concat(scenario == "glow" ? new[] { new MArgument("-Dorbit.client.world-smoke=true"), new MArgument("-Dorbit.client.glow-smoke=true") } : Array.Empty<MArgument>())
                .Concat(scenario is "ui" or "ui-scale" ? new[] { new MArgument("-Dorbit.client.visual-smoke=true"), new MArgument("-Dorbit.client.world-smoke=true") } : Array.Empty<MArgument>())
                .Concat(scenario == "ui-themes" ? new[] { new MArgument("-Dorbit.client.theme-smoke=true"), new MArgument("-Dorbit.client.world-smoke=true") } : Array.Empty<MArgument>())
                .Concat(scenario == "trajectories" ? new[] { new MArgument("-Dorbit.client.trajectory-smoke=true"), new MArgument("-Dorbit.client.world-smoke=true") } : Array.Empty<MArgument>())
                .Concat(ownSkin ? new[] { new MArgument("-Dorbit.client.offline-skin-smoke=true"), new MArgument("-Dorbit.client.world-smoke=true") } : Array.Empty<MArgument>())
                .Concat(scaleRegression ? new[] { new MArgument("-Dorbit.client.scale-smoke=true") } : Array.Empty<MArgument>()).ToArray()
        };
        using var process = await launcher.BuildProcessAsync("fabric-loader-0.19.5-26.2", options);
        process.StartInfo.WorkingDirectory = directory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = process.StartInfo.RedirectStandardError = true;
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
            await process.WaitForExitAsync(deadline.Token);
        } catch {
            if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) process.Kill(true); }
            throw;
        } finally {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await stderr);
        }
        var output = await stdout;
        var expected = refill ? "Stack Refill integration checks completed" : ownSkin ? "Offline skin integration checks completed" : scenario == "trajectories" ? "Trajectories integration checks completed" : scenario == "glow" ? "Enchantment Glow passed:" : "Orbit Client smoke checks completed";
        if (process.ExitCode != 0 || !output.Contains(expected))
            throw new Exception($"Full Fabric launch failed (exit {process.ExitCode}); see isolated stdout/stderr logs.");
        Console.WriteLine(scenario == "glow" ? "PASS real Minecraft glow: native/HMI/inventory/third person, toggles and settings; clean exit."
            : refill ? "PASS real Minecraft26.2/Fabric0.19.5: Stack Refill server transactions, no empty-hand frames, critical indicator assertions; clean exit."
            : "PASS full Minecraft 26.2 / Fabric 0.19.5 launch, installed mods, Ely agent and Orbit Client UI smoke; clean exit.");
    }
}
