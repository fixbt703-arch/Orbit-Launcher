using System;
using System.Collections.Generic;
using System.Linq;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class ModProjectDetails
{
	public string ProjectId { get; init; } = string.Empty;

	public string Slug { get; init; } = string.Empty;

	public string WebsiteUrl { get; init; } = string.Empty;

	public string Title { get; init; } = string.Empty;

	public string Summary { get; init; } = string.Empty;

	public string BodyText { get; init; } = string.Empty;

	public string ProjectType { get; init; } = "mod";

	public string Author { get; init; } = string.Empty;

	public string IconUrl { get; init; } = string.Empty;

	public long Downloads { get; init; }

	public long Followers { get; init; }

	public DateTime? UpdatedAt { get; init; }

	public string License { get; init; } = "Не указана";

	public string SourceUrl { get; init; } = string.Empty;

	public string IssuesUrl { get; init; } = string.Empty;

	public string VersionName { get; init; } = string.Empty;

	public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();

	public IReadOnlyList<ModDependencyInfo> Dependencies { get; init; } = Array.Empty<ModDependencyInfo>();

	public IReadOnlyList<ModGalleryImage> Gallery { get; init; } = Array.Empty<ModGalleryImage>();

	public ModSearchResult SearchResult { get; init; } = new ModSearchResult();

	public string StatsText
	{
		get
		{
			if (!LocalizationService.IsEnglish)
			{
				return FormatCompact(Downloads) + " загрузок  •  " + FormatCompact(Followers) + " подписчиков";
			}
			return FormatCompact(Downloads) + " downloads  •  " + FormatCompact(Followers) + " followers";
		}
	}

	public string CompatibilityText
	{
		get
		{
			if (VersionName.Length != 0)
			{
				if (!LocalizationService.IsEnglish)
				{
					return "Версия: " + VersionName;
				}
				return "Version: " + VersionName;
			}
			if (!LocalizationService.IsEnglish)
			{
				return "Для выбранных фильтров совместимая версия не найдена";
			}
			return "No compatible version was found for the selected filters";
		}
	}

	public string UpdatedText
	{
		get
		{
			if (UpdatedAt.HasValue)
			{
				if (LocalizationService.IsEnglish)
				{
					return $"Updated {UpdatedAt.Value.ToLocalTime():dd.MM.yyyy}";
				}
				return $"Обновлён {UpdatedAt.Value.ToLocalTime():dd.MM.yyyy}";
			}
			return string.Empty;
		}
	}

	public string ActionText => SearchResult.ActionText;

	public string AuthorText
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Author))
			{
				if (!LocalizationService.IsEnglish)
				{
					return "Автор: " + Author;
				}
				return "Author: " + Author;
			}
			if (!LocalizationService.IsEnglish)
			{
				return "Автор не указан";
			}
			return "Author not specified";
		}
	}

	public string LicenseText
	{
		get
		{
			if (!LocalizationService.IsEnglish)
			{
				return "Лицензия: " + License;
			}
			return "License: " + License;
		}
	}

	public string DependencyStatusText
	{
		get
		{
			int value = Dependencies.Count((ModDependencyInfo item) => string.Equals(item.Type, "required", StringComparison.OrdinalIgnoreCase));
			int value2 = Dependencies.Count((ModDependencyInfo item) => string.Equals(item.Type, "optional", StringComparison.OrdinalIgnoreCase));
			if (LocalizationService.IsEnglish)
			{
				string text = (string.IsNullOrWhiteSpace(VersionName) ? "the selected version" : ("version " + VersionName));
				if (Dependencies.Count != 0)
				{
					return $"For {text}: {value} required, {value2} optional.";
				}
				return "No dependencies are listed for " + text + ".";
			}
			string text2 = (string.IsNullOrWhiteSpace(VersionName) ? "выбранной версии" : ("версии " + VersionName));
			if (Dependencies.Count != 0)
			{
				return $"Для {text2}: обязательных — {value}, необязательных — {value2}.";
			}
			return "Для " + text2 + " зависимости не указаны.";
		}
	}

	private static string FormatCompact(long value)
	{
		if (value < 1000000)
		{
			if (value >= 1000)
			{
				return LocalizationService.IsEnglish ? $"{(double)value / 1000.0:0.#}K" : $"{(double)value / 1000.0:0.#} тыс.";
			}
			return value.ToString();
		}
		return LocalizationService.IsEnglish ? $"{(double)value / 1000000.0:0.#}M" : $"{(double)value / 1000000.0:0.#} млн";
	}
}
