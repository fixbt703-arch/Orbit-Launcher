using System.Diagnostics;
using System.Security.Cryptography;
using OrbitLauncher.Models;
using Microsoft.Win32;

namespace OrbitLauncher.Services;

public static class UpdateInstaller
{
    public static ProcessStartInfo PrepareStart(LauncherUpdate update, string installer, string executable, int processId, bool english)
    {
        executable = Path.GetFullPath(executable);
        installer = Path.GetFullPath(installer);
        if (!Path.GetFileName(executable).Equals("OrbitLauncher.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Автообновление доступно в собранном OrbitLauncher.exe.");
        using (var stream = File.OpenRead(installer))
            if (stream.Length != update.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Установщик изменился после загрузки. Повторите загрузку.");
        var fileVersion = FileVersionInfo.GetVersionInfo(installer).FileVersion;
        if (!GitHubUpdateService.TryParseVersion(fileVersion, out var version) || version != update.Version)
            throw new InvalidDataException("Версия установщика не совпадает с релизом GitHub. Автору нужно загрузить установщик этой версии.");
        var start = new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(installer)! };
        var allUsers = RequiresElevation(Path.GetDirectoryName(executable)!);
        if (allUsers) start.Verb = "runas";
        // Use ArgumentList, never concatenate paths into executable command text.
        foreach (var argument in new[] { "/SP-", "/SILENT", "/NORESTART", "/CLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
            "/ORBITUPDATE=1", "/ORBITUPDATEPID=" + processId, "/LANG=" + (english ? "english" : "russian"),
            "/DIR=" + Path.GetDirectoryName(executable), allUsers ? "/ALLUSERS" : "/CURRENTUSER", "/LOG" }) start.ArgumentList.Add(argument);
        return start;
    }

    private static bool RequiresElevation(string folder)
    {
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows })
        {
            var protectedRoot = Environment.GetFolderPath(special).TrimEnd(Path.DirectorySeparatorChar);
            if (protectedRoot.Length > 0 && (folder.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return true;
        }
        // Preserve the scope of an all-users installation in a custom folder, too.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{3FEAE432-8BE8-4E8C-A0F3-DB99A41F981B}_is1");
                if (uninstall?.GetValue("InstallLocation") is string location
                    && Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar).Equals(folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException) { }
        }
        return false;
    }
}
