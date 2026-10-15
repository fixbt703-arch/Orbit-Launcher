using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class ModSearchResult : ObservableObject
{
	private ImageSource? _icon;

	private CatalogInstallState _installState;

	public string ProjectId { get; init; } = string.Empty;

	public string SourceId { get; init; } = "modrinth";

	public string WebsiteUrl { get; init; } = string.Empty;

	public string Title { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string Author { get; init; } = string.Empty;

	public string AuthorByline
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Author))
			{
				return " by " + Author;
			}
			return string.Empty;
		}
	}

	public string IconUrl { get; init; } = string.Empty;

	public string ProjectType { get; init; } = "mod";

	public string LatestVersion { get; init; } = string.Empty;

	public DateTime? DateModified { get; init; }

	public long Downloads { get; init; }

	public long? Followers { get; init; }

	public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();

	public string Environment { get; init; } = string.Empty;

	public ImageSource? Icon
	{
		get
		{
			return _icon;
		}
		set
		{
			SetProperty(ref _icon, value, "Icon");
		}
	}

	public string ProjectTypeDisplay => LocalizationService.Translate(ProjectType switch
	{
		"modpack" => "Модпак", 
		"resourcepack" => "Ресурспак", 
		"shader" => "Шейдер", 
		"datapack" => "Датапак", 
		_ => "Мод", 
	});

	public CatalogInstallState InstallState
	{
		get
		{
			return _installState;
		}
		private set
		{
			if (SetProperty(ref _installState, value, "InstallState"))
			{
				OnPropertyChanged("ActionText");
				OnPropertyChanged("CanInstall");
			}
		}
	}

	public bool CanInstall
	{
		get
		{
			if (InstallState != CatalogInstallState.Available)
			{
				if (InstallState == CatalogInstallState.Downloaded && SourceId == "modrinth")
				{
					return ProjectType == "modpack";
				}
				return false;
			}
			return true;
		}
	}

	public string ActionText => LocalizationService.Translate(InstallState switch
	{
		CatalogInstallState.Downloading => "Загрузка…", 
		CatalogInstallState.Installed => "Установлен", 
		CatalogInstallState.Downloaded => (SourceId == "modrinth" && ProjectType == "modpack") ? "Установить" : "Скачан", 
		_ => (ProjectType == "datapack" || (ProjectType == "modpack" && SourceId != "modrinth")) ? "Скачать" : "Установить", 
	});

	public string DownloadsText
	{
		get
		{
			long downloads;
			if (LocalizationService.IsEnglish)
			{
				downloads = Downloads;
				return (downloads >= 1000000) ? $"{(double)Downloads / 1000000.0:0.#}M downloads" : ((downloads < 1000) ? $"{Downloads} downloads" : $"{(double)Downloads / 1000.0:0.#}K downloads");
			}
			downloads = Downloads;
			return (downloads >= 1000000) ? $"{(double)Downloads / 1000000.0:0.#} млн загрузок" : ((downloads < 1000) ? $"{Downloads} загрузок" : $"{(double)Downloads / 1000.0:0.#} тыс. загрузок");
		}
	}

	public string UpdatedText
	{
		get
		{
			if (DateModified.HasValue)
			{
				if (LocalizationService.IsEnglish)
				{
					return $"Updated {DateModified.Value.ToLocalTime():dd.MM.yyyy}";
				}
				return $"Обновлён {DateModified.Value.ToLocalTime():dd.MM.yyyy}";
			}
			return string.Empty;
		}
	}

	public string CompactDownloads => CompactCount(Downloads);

	public string CompactFollowers
	{
		get
		{
			if (Followers.HasValue)
			{
				return CompactCount(Followers.Value);
			}
			return string.Empty;
		}
	}

	public bool HasFollowers => Followers.HasValue;

	public bool HasUpdatedDate => DateModified.HasValue;

	public string UpdatedShortText => DateModified?.ToLocalTime().ToString("dd.MM.yyyy") ?? string.Empty;

	public IReadOnlyList<CatalogChip> Chips => ((Environment.Length <= 0) ? ((IEnumerable<CatalogChip>)Array.Empty<CatalogChip>()) : ((IEnumerable<CatalogChip>)new CatalogChip[1]
	{
		new CatalogChip(LocalizationService.Translate(Environment), IsLoader: false)
	})).Concat(Categories.Select((string category) => new CatalogChip(LocalizationService.Translate(category), IsLoader: false))).Concat(Loaders.Select((string loader) => new CatalogChip(loader, IsLoader: true))).ToList();

	public void SetInstallState(CatalogInstallState state)
	{
		InstallState = state;
	}

	private static string CompactCount(long count)
	{
		if (LocalizationService.IsEnglish)
		{
			return (count >= 1000000) ? $"{(double)count / 1000000.0:0.#}M" : ((count < 10000) ? count.ToString("N0") : $"{(double)count / 1000.0:0.#}K");
		}
		return (count >= 1000000) ? $"{(double)count / 1000000.0:0.#} млн" : ((count < 10000) ? count.ToString("N0") : $"{(double)count / 1000.0:0.#} тыс.");
	}
}
