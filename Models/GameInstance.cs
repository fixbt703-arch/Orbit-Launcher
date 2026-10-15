using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class GameInstance : ObservableObject
{
	private string _name = "Новая сборка";

	private string _gameVersion = "1.21.1";

	private string _loader = "Vanilla";

	private string _loaderVersion = string.Empty;

	private string _launchVersionId = string.Empty;

	private int _minimumRamMb = 1024;

	private int _maximumRamMb = 4096;

	private string _javaPath = string.Empty;

	private string _iconFile = string.Empty;

	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			SetProperty(ref _name, value, "Name");
		}
	}

	public string GameVersion
	{
		get
		{
			return _gameVersion;
		}
		set
		{
			SetProperty(ref _gameVersion, value, "GameVersion");
		}
	}

	public string Loader
	{
		get
		{
			return _loader;
		}
		set
		{
			SetProperty(ref _loader, value, "Loader");
		}
	}

	public string LoaderVersion
	{
		get
		{
			return _loaderVersion;
		}
		set
		{
			SetProperty(ref _loaderVersion, value, "LoaderVersion");
		}
	}

	public string LaunchVersionId
	{
		get
		{
			return _launchVersionId;
		}
		set
		{
			SetProperty(ref _launchVersionId, value, "LaunchVersionId");
		}
	}

	public int MinimumRamMb
	{
		get
		{
			return _minimumRamMb;
		}
		set
		{
			SetProperty(ref _minimumRamMb, value, "MinimumRamMb");
		}
	}

	public int MaximumRamMb
	{
		get
		{
			return _maximumRamMb;
		}
		set
		{
			SetProperty(ref _maximumRamMb, value, "MaximumRamMb");
		}
	}

	public string JavaPath
	{
		get
		{
			return _javaPath;
		}
		set
		{
			SetProperty(ref _javaPath, value, "JavaPath");
		}
	}

	public string IconFile
	{
		get
		{
			return _iconFile;
		}
		set
		{
			SetProperty(ref _iconFile, value, "IconFile");
		}
	}

	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public DateTime? LastPlayedAt { get; set; }

	public long TotalPlaySeconds { get; set; }

	public bool OrbitClientEnabled { get; set; }

	[JsonIgnore]
	public string DirectoryPath { get; set; } = string.Empty;

	[JsonIgnore]
	public string ModsPath => Path.Combine(DirectoryPath, "mods");

	[JsonIgnore]
	public string IconPath
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(IconFile))
			{
				return Path.Combine(DirectoryPath, IconFile);
			}
			return string.Empty;
		}
	}

	[JsonIgnore]
	public string LoaderDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(LoaderVersion) && !(Loader == "Vanilla"))
			{
				return Loader + " " + LoaderVersion;
			}
			return Loader;
		}
	}

	[JsonIgnore]
	public string Subtitle => "Minecraft " + GameVersion + "  •  " + LoaderDisplay + (OrbitClientEnabled ? "  •  Orbit Client" : string.Empty);

	[JsonIgnore]
	public string LastPlayedText
	{
		get
		{
			if (LastPlayedAt.HasValue)
			{
				if (LocalizationService.IsEnglish)
				{
					return $"Played: {LastPlayedAt.Value.ToLocalTime():dd.MM.yyyy}";
				}
				return $"Запуск: {LastPlayedAt.Value.ToLocalTime():dd.MM.yyyy}";
			}
			if (!LocalizationService.IsEnglish)
			{
				return "Ещё не запускалась";
			}
			return "Never played";
		}
	}

	[JsonIgnore]
	public string LoaderBadge => Loader.ToUpperInvariant();

	[JsonIgnore]
	public int ModCount
	{
		get
		{
			if (!Directory.Exists(ModsPath))
			{
				return 0;
			}
			return Directory.EnumerateFiles(ModsPath).Count((string path) => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));
		}
	}

	[JsonIgnore]
	public string ModCountText
	{
		get
		{
			if (ModCount != 0)
			{
				if (LocalizationService.IsEnglish)
				{
					return $"Mods: {ModCount}";
				}
				return $"Модов: {ModCount}";
			}
			return LocalizationService.Translate("Без модов");
		}
	}

	public GameInstance CloneDefinition()
	{
		return new GameInstance
		{
			Name = Name,
			GameVersion = GameVersion,
			Loader = Loader,
			LoaderVersion = LoaderVersion,
			OrbitClientEnabled = OrbitClientEnabled,
			LaunchVersionId = LaunchVersionId,
			MinimumRamMb = MinimumRamMb,
			MaximumRamMb = MaximumRamMb,
			JavaPath = JavaPath,
			CreatedAt = DateTime.UtcNow
		};
	}
}
