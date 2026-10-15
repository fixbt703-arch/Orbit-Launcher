using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed record ModCategoryOption(string Id, string DisplayName)
{
	public string LocalizedDisplayName => LocalizationService.Translate(DisplayName);
}
