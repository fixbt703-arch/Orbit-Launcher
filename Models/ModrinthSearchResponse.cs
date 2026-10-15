using System.Collections.Generic;

namespace OrbitLauncher.Models;

public sealed record ModrinthSearchResponse(IReadOnlyList<ModSearchResult> Items, int TotalHits);
