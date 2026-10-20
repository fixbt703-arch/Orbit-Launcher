namespace OrbitLauncher.Models;

public sealed record LauncherUpdate(Version Version, string Tag, string Notes, Uri ReleaseUrl,
    Uri DownloadUrl, long Size, string Sha256);

public sealed record UpdateDownloadProgress(long Received, long Total)
{
    public double Percent => Math.Clamp(Received * 100d / Total, 0, 100);
}
