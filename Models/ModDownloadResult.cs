namespace OrbitLauncher.Models;

public sealed record ModDownloadResult(string DestinationPath, string Message, bool InstalledDirectly);
