using System;
using System.Collections.Generic;
using System.Linq;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed record CatalogVersion(string Id, string Name, string FileName, string Channel, DateTime? PublishedAt, IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders)
{
	public string ChannelText => LocalizationService.Translate(Channel switch
	{
		"release" => "Релиз", 
		"beta" => "Бета", 
		"alpha" => "Альфа", 
		_ => Channel, 
	});

	public string CompatibilityText => string.Join(" · ", GameVersions.Take(4).Concat(Loaders.Take(2)));

	public string PublishedText => PublishedAt?.ToLocalTime().ToString("dd.MM.yyyy") ?? string.Empty;
}
