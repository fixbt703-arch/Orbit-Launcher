using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class VersionPickerWindow : Window
{
	private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<CatalogVersion>>> _fetch;

	private readonly GameInstance _instance;

	private readonly ModProjectDetails _project;

	private readonly CancellationTokenSource _closed = new CancellationTokenSource();

	private CancellationTokenSource? _load;

	private bool _initializing = true;

	private IReadOnlyList<CatalogVersion> _allVersions = Array.Empty<CatalogVersion>();


	public ObservableCollection<CatalogVersion> Versions { get; } = new ObservableCollection<CatalogVersion>();

	public CatalogVersion? SelectedVersion { get; private set; }

	public VersionPickerWindow(GameInstance instance, ModProjectDetails project, Func<string, string, CancellationToken, Task<IReadOnlyList<CatalogVersion>>> fetch)
	{
		InitializeComponent();
		_instance = instance;
		_project = project;
		_fetch = fetch;
		VersionsList.ItemsSource = Versions;
		ProjectText.Text = project.Title;
		GameVersionCombo.ItemsSource = new ModSearchOption[1]
		{
			new ModSearchOption("", "Все версии")
		}.Concat(from item in project.GameVersions.Append(instance.GameVersion).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			select new ModSearchOption(item, item)).ToArray();
		string projectType = project.ProjectType;
		bool flag = ((projectType == "mod" || projectType == "modpack") ? true : false);
		LoaderCombo.ItemsSource = new ModSearchOption[1]
		{
			new ModSearchOption("", "Все загрузчики")
		}.Concat(from item in (from item in project.Loaders.Append(flag ? instance.Loader : string.Empty)
				where !string.IsNullOrWhiteSpace(item) && !item.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)
				select item).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			select new ModSearchOption(item.ToLowerInvariant(), item)).ToArray();
		GameVersionCombo.SelectedValue = instance.GameVersion;
		string text = (flag ? instance.Loader.ToLowerInvariant() : string.Empty);
		LoaderCombo.SelectedValue = ((text == "vanilla") ? "" : text);
		LoaderCombo.IsEnabled = flag;
		_initializing = false;
		LocalizationService.Apply(this);
		base.Loaded += async delegate
		{
			await ReloadAsync();
		};
		base.Closed += delegate
		{
			_closed.Cancel();
			_load?.Cancel();
			_load?.Dispose();
			_closed.Dispose();
		};
	}

	private async Task ReloadAsync()
	{
		_load?.Cancel();
		_load?.Dispose();
		_load = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
		CancellationToken token = _load.Token;
		_allVersions = Array.Empty<CatalogVersion>();
		Versions.Clear();
		InstallButton.IsEnabled = false;
		LoadingPanel.Visibility = Visibility.Visible;
		EmptyPanel.Visibility = Visibility.Collapsed;
		try
		{
			string arg = GameVersionCombo.SelectedValue?.ToString() ?? "";
			string arg2 = LoaderCombo.SelectedValue?.ToString() ?? "";
			_allVersions = await _fetch(arg, arg2, token);
			if (!token.IsCancellationRequested)
			{
				ApplyChannelFilter();
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!token.IsCancellationRequested)
			{
				StatusText.Text = ex2.Message;
			}
		}
		finally
		{
			if (!token.IsCancellationRequested)
			{
				LoadingPanel.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void ApplyChannelFilter()
	{
		string channel = (ChannelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
		Versions.Clear();
		foreach (CatalogVersion item in _allVersions.Where((CatalogVersion item) => channel == "all" || item.Channel == channel))
		{
			Versions.Add(item);
		}
		string text = GameVersionCombo.SelectedValue?.ToString() ?? "";
		string text2 = LoaderCombo.SelectedValue?.ToString() ?? "";
		bool flag = !string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(text2);
		EmptyPanel.Visibility = ((Versions.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		ShowAllVersionsButton.Visibility = ((!(Versions.Count == 0 && flag)) ? Visibility.Collapsed : Visibility.Visible);
		ShowAllVersionsButton.Content = (LocalizationService.IsEnglish ? "Show all game versions and loaders" : "Показать все версии игры и загрузчики");
		if (Versions.Count == 0)
		{
			string text3 = channel switch
			{
				"alpha" => LocalizationService.IsEnglish ? "alpha builds" : "альфа-версий", 
				"beta" => LocalizationService.IsEnglish ? "beta builds" : "бета-версий", 
				"release" => LocalizationService.IsEnglish ? "releases" : "релизов", 
				_ => LocalizationService.IsEnglish ? "files" : "файлов", 
			};
			string value = string.Join(" + ", new string[2] { text, text2 }.Where((string value2) => !string.IsNullOrWhiteSpace(value2)));
			EmptyText.Text = ((!flag) ? (LocalizationService.IsEnglish ? ("This project has no " + text3 + ".") : ("У этого проекта нет " + text3 + ".")) : (LocalizationService.IsEnglish ? $"No {text3} for {value}. The project may have {text3} for other Minecraft versions or loaders." : $"Для {value} нет {text3}. У проекта могут быть {text3} для других версий Minecraft или загрузчиков."));
		}
		StatusText.Text = (LocalizationService.IsEnglish ? $"{Versions.Count} versions" : $"Версий: {Versions.Count}");
	}

	private async void ShowAllVersions_Click(object sender, RoutedEventArgs e)
	{
		_initializing = true;
		GameVersionCombo.SelectedValue = "";
		LoaderCombo.SelectedValue = "";
		_initializing = false;
		await ReloadAsync();
	}

	private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (!_initializing && base.IsLoaded)
		{
			await ReloadAsync();
		}
	}

	private void Channel_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (!_initializing && base.IsLoaded)
		{
			ApplyChannelFilter();
		}
	}

	private void Version_Changed(object sender, SelectionChangedEventArgs e)
	{
		CatalogVersion? catalogVersion = VersionsList.SelectedItem as CatalogVersion;
		bool flag = catalogVersion is not null && (_project.ProjectType == "modpack" || (catalogVersion.GameVersions.Contains<string>(_instance.GameVersion, StringComparer.OrdinalIgnoreCase) && (_project.ProjectType != "mod" || catalogVersion.Loaders.Count == 0 || catalogVersion.Loaders.Contains<string>(_instance.Loader, StringComparer.OrdinalIgnoreCase))));
		InstallButton.IsEnabled = flag;
		if (catalogVersion is not null && !flag)
		{
			StatusText.Text = (LocalizationService.IsEnglish ? "This file is not compatible with the selected instance." : "Этот файл несовместим с выбранной сборкой.");
		}
	}

	private void Install_Click(object sender, RoutedEventArgs e)
	{
		if (VersionsList.SelectedItem is CatalogVersion selectedVersion && InstallButton.IsEnabled)
		{
			SelectedVersion = selectedVersion;
			base.DialogResult = true;
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed && !(e.OriginalSource is Button))
		{
			DragMove();
		}
	}








}
