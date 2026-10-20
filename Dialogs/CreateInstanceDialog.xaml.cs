using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class CreateInstanceDialog : Window
{
	private readonly VersionManifestService _versionService;

	private readonly LoaderVersionService _loaderVersionService;

	private readonly LauncherSettings _settings;

	private IReadOnlyList<MinecraftVersionInfo> _versions = Array.Empty<MinecraftVersionInfo>();

	private CancellationTokenSource? _loaderVersionsCancellation;

	private bool _changingOrbitClientSelection;
	private bool _closed;
	private readonly CancellationTokenSource _lifetime = new();


	public ObservableCollection<MinecraftVersionInfo> VisibleVersions { get; } = new ObservableCollection<MinecraftVersionInfo>();

	public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = new ObservableCollection<LoaderVersionInfo>();

	public GameInstance? Result { get; private set; }

	public string? SelectedIconPath { get; private set; }

	private string SelectedLoader => new RadioButton[5] { VanillaLoader, FabricLoader, ForgeLoader, NeoForgeLoader, QuiltLoader }.FirstOrDefault((RadioButton button) => button.IsChecked == true)?.Tag?.ToString() ?? "Vanilla";

	public CreateInstanceDialog(HttpClient httpClient, LauncherSettings settings)
	{
		InitializeComponent();
		_versionService = new VersionManifestService(httpClient, settings.MinecraftManifestApi);
		_loaderVersionService = new LoaderVersionService(httpClient, settings);
		_settings = settings;
		MinRamBox.Text = settings.DefaultMinimumRamMb.ToString();
		MaxRamBox.Text = settings.DefaultMaximumRamMb.ToString();
		base.DataContext = this;
		LocalizationService.Apply(this);
		base.Loaded += CreateInstanceDialog_Loaded;
		base.Closed += delegate
		{
			_closed = true;
			_lifetime.Cancel();
			_lifetime.Dispose();
			_loaderVersionsCancellation?.Cancel();
			_loaderVersionsCancellation?.Dispose();
		};
	}

	private async void CreateInstanceDialog_Loaded(object sender, RoutedEventArgs e)
	{
		CreateButton.IsEnabled = false;
		OrbitClientCheck.IsEnabled = false;
		try
		{
			_versions = await _versionService.GetVersionsAsync(_settings.ShowSnapshots, _lifetime.Token);
			if (_closed) return;
			VersionLoadingText.Text = (LocalizationService.IsEnglish ? $"{_versions.Count} versions" : $"{_versions.Count} версий");
		}
		catch
		{
			if (_closed) return;
			_versions = new string[]
			{
				"26.2", "26.1.2", "1.21.8", "1.21.7", "1.21.6", "1.21.5", "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4",
				"1.18.2", "1.16.5"
			}.Select((string id) => new MinecraftVersionInfo
			{
				Id = id,
				ReleaseTime = DateTime.Today
			}).ToList();
			VersionLoadingText.Text = LocalizationService.Translate("Резервный список");
		}
		ApplyVersionFilter();
		VersionList.SelectedIndex = 0;
		await RefreshLoaderVersionsAsync();
		if (_closed) return;
		OrbitClientCheck.IsEnabled = true;
		LocalizationService.Apply(this);
	}

	private void VersionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (base.IsLoaded)
		{
			ApplyVersionFilter();
		}
	}

	private async void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (base.IsLoaded)
		{
			await RefreshLoaderVersionsAsync();
		}
	}

	private async void Loader_Checked(object sender, RoutedEventArgs e)
	{
		if (base.IsLoaded)
		{
			await RefreshLoaderVersionsAsync();
		}
	}

	private async void OrbitClientCheck_Changed(object sender, RoutedEventArgs e)
	{
		if (!base.IsLoaded || _changingOrbitClientSelection)
		{
			return;
		}
		_changingOrbitClientSelection = true;
		try
		{
			bool valueOrDefault = OrbitClientCheck.IsChecked == true;
			ApplyOrbitClientLock(valueOrDefault);
			if (!valueOrDefault)
			{
				return;
			}
			VersionSearchBox.Text = string.Empty;
			MinecraftVersionInfo? minecraftVersionInfo = _versions.FirstOrDefault((MinecraftVersionInfo version) => version.Id == "26.2");
			if (minecraftVersionInfo == null)
			{
				OrbitClientCheck.IsChecked = false;
				ApplyOrbitClientLock(enabled: false);
				LocalizationService.Show(this, "Minecraft 26.2 отсутствует в списке версий. Обновите список и попробуйте снова.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			VersionList.SelectedItem = minecraftVersionInfo;
			FabricLoader.IsChecked = true;
			await RefreshLoaderVersionsAsync();
			if (_closed) return;
			if (OrbitClientCheck.IsChecked != valueOrDefault) { ApplyOrbitClientLock(OrbitClientCheck.IsChecked == true); return; }
			Version? result;
			LoaderVersionInfo? loaderVersionInfo = LoaderVersions.FirstOrDefault((LoaderVersionInfo version) => Version.TryParse(version.Id, out result) && result >= new Version(0, 19, 3));
			if (loaderVersionInfo == null)
			{
				OrbitClientCheck.IsChecked = false;
				ApplyOrbitClientLock(enabled: false);
				LocalizationService.Show(this, "Не удалось найти Fabric Loader 0.19.3 или новее для Minecraft 26.2.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			else
			{
				LoaderVersionCombo.SelectedItem = loaderVersionInfo;
			}
		}
		finally
		{
			_changingOrbitClientSelection = false;
		}
	}

	private void ApplyOrbitClientLock(bool enabled)
	{
		VersionSearchBox.IsEnabled = !enabled;
		VersionList.IsEnabled = !enabled;
		VanillaLoader.IsEnabled = !enabled;
		ForgeLoader.IsEnabled = !enabled;
		NeoForgeLoader.IsEnabled = !enabled;
		QuiltLoader.IsEnabled = !enabled;
	}

	private async Task RefreshLoaderVersionsAsync()
	{
		if (_closed) return;
		_loaderVersionsCancellation?.Cancel();
		_loaderVersionsCancellation?.Dispose();
		_loaderVersionsCancellation = new CancellationTokenSource();
		var request = _loaderVersionsCancellation;
		var token = request.Token;
		CreateButton.IsEnabled = false;
		LoaderVersions.Clear();
		LoaderVersionCombo.IsEnabled = false;
		string selectedLoader = SelectedLoader;
		string gameVersion = (VersionList.SelectedItem as MinecraftVersionInfo)?.Id ?? string.Empty;
		if (selectedLoader == "Vanilla")
		{
			LoaderVersionStatusText.Text = LocalizationService.Translate("Для Vanilla не требуется");
			CreateButton.IsEnabled = true;
			return;
		}
		LoaderVersionStatusText.Text = LocalizationService.Translate("Загрузка версий…");
		try
		{
			var versions = await _loaderVersionService.GetVersionsAsync(selectedLoader, gameVersion, token);
			if (_closed || token.IsCancellationRequested || !ReferenceEquals(request, _loaderVersionsCancellation)) return;
			foreach (LoaderVersionInfo item in versions)
			{
				LoaderVersions.Add(item);
			}
			LoaderVersionCombo.SelectedIndex = ((LoaderVersions.Count <= 0) ? (-1) : 0);
			LoaderVersionCombo.IsEnabled = LoaderVersions.Count > 0;
			LoaderVersionStatusText.Text = ((LoaderVersions.Count > 0) ? $"Доступно: {LoaderVersions.Count}" : "Нет совместимых версий");
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			if (_closed || token.IsCancellationRequested || !ReferenceEquals(request, _loaderVersionsCancellation)) return;
			LoaderVersionStatusText.Text = LocalizationService.Translate("Не удалось получить список");
		}
		finally
		{
			if (!_closed && ReferenceEquals(request, _loaderVersionsCancellation)) CreateButton.IsEnabled = LoaderVersionCombo.SelectedItem is LoaderVersionInfo;
		}
	}

	private void ApplyVersionFilter()
	{
		string query = VersionSearchBox.Text.Trim();
		string? selectedId = (VersionList.SelectedItem as MinecraftVersionInfo)?.Id;
		VisibleVersions.Clear();
		foreach (MinecraftVersionInfo item in _versions.Where((MinecraftVersionInfo version) => string.IsNullOrWhiteSpace(query) || version.Id.Contains(query, StringComparison.OrdinalIgnoreCase)))
		{
			VisibleVersions.Add(item);
		}
		VersionList.SelectedItem = VisibleVersions.FirstOrDefault((MinecraftVersionInfo version) => version.Id == selectedId) ?? VisibleVersions.FirstOrDefault();
	}

	private void ChooseIcon_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(openFileDialog.FileName, UriKind.Absolute);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			IconPreview.Source = bitmapImage;
			DefaultIconGlyph.Visibility = Visibility.Collapsed;
			SelectedIconPath = openFileDialog.FileName;
		}
		catch (Exception ex)
		{
			LocalizationService.Show(this, "Не удалось открыть изображение.\n\n" + ex.Message, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void Create_Click(object sender, RoutedEventArgs e)
	{
		string text = NameBox.Text.Trim();
		string text2 = (VersionList.SelectedItem as MinecraftVersionInfo)?.Id ?? string.Empty;
		string selectedLoader = SelectedLoader;
		string text3 = (LoaderVersionCombo.SelectedItem as LoaderVersionInfo)?.Id ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
		{
			LocalizationService.Show(this, "Укажите название и выберите версию Minecraft.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (selectedLoader != "Vanilla" && string.IsNullOrWhiteSpace(text3))
		{
			LocalizationService.Show(this, "Выберите версию загрузчика. Если список пуст, эта версия Minecraft не поддерживается выбранным загрузчиком.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (OrbitClientCheck.IsChecked == true && !OrbitClientService.Supports(text2, selectedLoader))
		{
			LocalizationService.Show(this, "Orbit Client поддерживает только Minecraft 26.2 + Fabric.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (OrbitClientCheck.IsChecked == true && (!Version.TryParse(text3, out var result) || result < new Version(0, 19, 3)))
		{
			LocalizationService.Show(this, "Orbit Client требует Fabric Loader 0.19.3 или новее.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!int.TryParse(MinRamBox.Text, out var result2) || !int.TryParse(MaxRamBox.Text, out var result3) || result2 < 512 || result3 < result2 || result3 > 65536)
		{
			LocalizationService.Show(this, "Проверьте объём памяти: минимум 512 МБ, максимум не меньше минимума.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		Result = new GameInstance
		{
			Name = text,
			GameVersion = text2,
			Loader = selectedLoader,
			LoaderVersion = text3,
			OrbitClientEnabled = (OrbitClientCheck.IsChecked == true),
			OrbitConnectEnabled = (OrbitConnectCheck.IsChecked == true),
			MinimumRamMb = result2,
			MaximumRamMb = result3
		};
		base.DialogResult = true;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		DragMove();
	}








}
