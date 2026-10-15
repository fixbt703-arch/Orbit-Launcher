namespace OrbitLauncher.Models;

public sealed record ModrinthSearchRequest(string Query, string ProjectType, string GameVersion, string Loader, string Category, string Environment, string Index, int Offset = 0, int Limit = 50);
