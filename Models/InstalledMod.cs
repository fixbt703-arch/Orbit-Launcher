using System;
using System.Windows.Media;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class InstalledMod : ObservableObject
{
	private string _name = string.Empty;

	private ImageSource? _icon;

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

	public string FileName { get; init; } = string.Empty;

	public string FullPath { get; init; } = string.Empty;

	public bool IsEnabled { get; init; }

	public long SizeBytes { get; init; }

	public string TypeText { get; init; } = "Мод";

	public string LocalizedTypeText => LocalizationService.Translate(TypeText);

	public DateTime ModifiedAt { get; init; }

	public string FolderText { get; init; } = string.Empty;

	public bool CanToggle { get; init; } = true;

	public string StateText => LocalizationService.Translate(IsEnabled ? "Включён" : "Отключён");

	public string ToggleActionText => LocalizationService.Translate(IsEnabled ? "Выключить" : "Включить");

	public string ModifiedText
	{
		get
		{
			if (LocalizationService.IsEnglish)
			{
				return $"Modified {ModifiedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
			}
			return $"Изменён {ModifiedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
		}
	}

	public string IconGlyph => TypeText switch
	{
		"Ресурспак" => "\ueef7", 
		"Шейдер" => "\ueb30", 
		"Скачанный набор" => "\uea96", 
		_ => "\uf2c9", 
	};

	public string SizeText
	{
		get
		{
			if (SizeBytes >= 1048576)
			{
				return $"{(double)SizeBytes / 1024.0 / 1024.0:0.0} МБ";
			}
			return $"{Math.Max(1L, SizeBytes / 1024)} КБ";
		}
	}
}
