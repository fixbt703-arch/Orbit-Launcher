using System;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class MinecraftVersionInfo
{
	public string Id { get; init; } = string.Empty;

	public string Type { get; init; } = "release";

	public DateTime ReleaseTime { get; init; }

	public string DisplayName
	{
		get
		{
			if (!(Type == "release"))
			{
				return Id + "  (" + Type + ")";
			}
			return Id;
		}
	}

	public string TypeDisplay => LocalizationService.Translate(Type switch
	{
		"release" => "Релиз", 
		"snapshot" => "Снапшот", 
		"old_beta" => "Beta", 
		"old_alpha" => "Alpha", 
		_ => Type, 
	});
}
