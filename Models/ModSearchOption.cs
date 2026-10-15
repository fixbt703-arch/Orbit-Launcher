using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed record ModSearchOption(string Id, string DisplayName)
{
	public string LocalizedDisplayName => LocalizationService.Translate(DisplayName);
}
