using System.Security.AccessControl;
using System.Security.Principal;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services.Connect;

internal static class ConnectDirectories
{
    public static string CreatePrivate(string category)
    {
        string path = Path.Combine(AppPaths.Root, "connect", category, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl); return path;
    }
    public static void DeleteOwned(string path, string category)
    {
        string root = Path.GetFullPath(Path.Combine(AppPaths.Root, "connect", category)) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _)) return;
        try { if (Directory.Exists(resolved) && !new DirectoryInfo(resolved).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(resolved, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
