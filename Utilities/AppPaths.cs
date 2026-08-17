namespace OrbitLauncher.Utilities;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OrbitLauncher");

    public static string Instances { get; } = Path.Combine(Root, "instances");
    public static string Cache { get; } = Path.Combine(Root, "cache");
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string AccountsFile { get; } = Path.Combine(Root, "accounts.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Instances);
        Directory.CreateDirectory(Cache);
    }
}
