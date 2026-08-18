namespace OrbitLauncher.Models;

public sealed class LoaderVersionInfo
{
    public string Id { get; init; } = string.Empty;
    public bool IsStable { get; init; } = true;
    public string DisplayName => IsStable ? Id : $"{Id}  (beta)";
}
