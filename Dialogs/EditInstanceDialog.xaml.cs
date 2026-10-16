using System;
using System.CodeDom.Compiler;
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
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class EditInstanceDialog : Window
{
	private readonly GameInstance _instance;

	private readonly LoaderVersionService _loaderVersionService;

	private readonly string _originalGameVersion;

	private readonly string _originalLoader;

	private readonly string _originalLoaderVersion;

	private readonly string _originalLaunchVersionId;

	private CancellationTokenSource? _refreshCancellation;

	private bool _updatingOrbitClient;
	private bool _closed;


	public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = new ObservableCollection<LoaderVersionInfo>();

	private string SelectedLoader => (LoaderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";

	public EditInstanceDialog(HttpClient httpClient, LauncherSettings settings, GameInstance instance)
	{
		InitializeComponent();
		_instance = instance;
		_loaderVersionService = new LoaderVersionService(httpClient, settings);
		_originalGameVersion = instance.GameVersion;
		_originalLoader = instance.Loader;
		_originalLoaderVersion = instance.LoaderVersion;
		_originalLaunchVersionId = instance.LaunchVersionId;
		base.DataContext = this;
		NameBox.Text = instance.Name;
		GameVersionBox.Text = instance.GameVersion;
		ComboBox loaderBox = LoaderBox;
		loaderBox.SelectedIndex = instance.Loader switch
		{
			"Fabric" => 1, 
			"Forge" => 2, 
			"NeoForge" => 3, 
			"Quilt" => 4, 
			_ => 0, 
		};
		MinRamBox.Text = instance.MinimumRamMb.ToString();
		MaxRamBox.Text = instance.MaximumRamMb.ToString();
		JavaPathBox.Text = instance.JavaPath;
		LaunchVersionBox.Text = instance.LaunchVersionId;
		OrbitClientCheck.IsChecked = instance.OrbitClientEnabled;
		ApplyOrbitClientLock(instance.OrbitClientEnabled);
		LocalizationService.Apply(this);
		base.Loaded += async delegate
		{
			await RefreshLoaderVersionsAsync(_originalLoaderVersion);
			LocalizationService.Apply(this);
		};
		base.Closed += delegate
		{
			_closed = true;
			_refreshCancellation?.Cancel();
			_refreshCancellation?.Dispose();
		};
	}

	private async void LoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (base.IsLoaded)
		{
			await RefreshLoaderVersionsAsync();
		}
	}

	private async void OrbitClientCheck_Changed(object sender, RoutedEventArgs e)
	{
		if (!base.IsLoaded || _updatingOrbitClient)
		{
			return;
		}
		_updatingOrbitClient = true;
		try
		{
			bool enabled = OrbitClientCheck.IsChecked == true;
			if (enabled)
			{
				GameVersionBox.Text = "26.2";
				LoaderBox.SelectedIndex = 1;
				await RefreshLoaderVersionsAsync();
				if (_closed) return;
				if (OrbitClientCheck.IsChecked != enabled) { ApplyOrbitClientLock(OrbitClientCheck.IsChecked == true); return; }
				Version result;
				LoaderVersionInfo loaderVersionInfo = LoaderVersions.FirstOrDefault((LoaderVersionInfo version) => Version.TryParse(version.Id, out result) && result >= new Version(0, 19, 3));
				if (loaderVersionInfo == null)
				{
					OrbitClientCheck.IsChecked = false;
					ApplyOrbitClientLock(enabled: false);
					LocalizationService.Show(this, "Не удалось найти Fabric Loader 0.19.3 или новее для Minecraft 26.2.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					return;
				}
				LoaderVersionBox.SelectedItem = loaderVersionInfo;
			}
			ApplyOrbitClientLock(enabled);
		}
		finally
		{
			_updatingOrbitClient = false;
		}
	}

	private void ApplyOrbitClientLock(bool enabled)
	{
		GameVersionBox.IsEnabled = !enabled;
		LoaderBox.IsEnabled = !enabled;
		LaunchVersionBox.IsEnabled = !enabled;
	}

	private async void GameVersionBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (base.IsLoaded)
		{
			await RefreshLoaderVersionsAsync(debounceMilliseconds: 350);
		}
	}

	private async Task RefreshLoaderVersionsAsync(string? selectVersion = null, int debounceMilliseconds = 0)
	{
		if (_closed) return;
		_refreshCancellation?.Cancel();
		_refreshCancellation?.Dispose();
		_refreshCancellation = new CancellationTokenSource();
		var request = _refreshCancellation;
		var token = request.Token;
		SaveButton.IsEnabled = false;
		LoaderVersions.Clear();
		LoaderVersionBox.IsEnabled = false;
		string selectedLoader = SelectedLoader;
		string gameVersion = GameVersionBox.Text.Trim();
		if (selectedLoader == "Vanilla")
		{
			LoaderVersionStatusText.Text = LocalizationService.Translate("Не требуется");
			SaveButton.IsEnabled = true;
			return;
		}
		LoaderVersionStatusText.Text = LocalizationService.Translate("Загрузка…");
		try
		{
			if (debounceMilliseconds > 0) await Task.Delay(debounceMilliseconds, token);
			var versions = await _loaderVersionService.GetVersionsAsync(selectedLoader, gameVersion, token);
			if (_closed || token.IsCancellationRequested || !ReferenceEquals(request, _refreshCancellation)) return;
			foreach (LoaderVersionInfo item in versions)
			{
				LoaderVersions.Add(item);
			}
			LoaderVersionBox.IsEnabled = LoaderVersions.Count > 0;
			LoaderVersionBox.SelectedValue = selectVersion ?? _instance.LoaderVersion;
			if (LoaderVersionBox.SelectedIndex < 0 && LoaderVersions.Count > 0)
			{
				LoaderVersionBox.SelectedIndex = 0;
			}
			LoaderVersionStatusText.Text = ((LoaderVersions.Count > 0) ? $"Доступно: {LoaderVersions.Count}" : "Нет совместимых версий");
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			if (_closed || token.IsCancellationRequested || !ReferenceEquals(request, _refreshCancellation)) return;
			if (gameVersion == _originalGameVersion && selectedLoader == _originalLoader && !string.IsNullOrWhiteSpace(_originalLoaderVersion))
			{
				LoaderVersions.Add(new LoaderVersionInfo { Id = _originalLoaderVersion });
				LoaderVersionBox.SelectedIndex = 0;
				LoaderVersionBox.IsEnabled = true;
			}
			LoaderVersionStatusText.Text = LocalizationService.Translate("Список недоступен");
		}
		finally
		{
			if (!_closed && ReferenceEquals(request, _refreshCancellation)) SaveButton.IsEnabled = LoaderVersionBox.SelectedItem is LoaderVersionInfo;
		}
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		string text = NameBox.Text.Trim();
		string text2 = GameVersionBox.Text.Trim();
		string selectedLoader = SelectedLoader;
		string text3 = (LoaderVersionBox.SelectedItem as LoaderVersionInfo)?.Id ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
		{
			LocalizationService.Show(this, "Название и версия Minecraft не могут быть пустыми.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (selectedLoader != "Vanilla" && string.IsNullOrWhiteSpace(text3))
		{
			LocalizationService.Show(this, "Выберите совместимую версию загрузчика.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (OrbitClientCheck.IsChecked == true && !OrbitClientService.Supports(text2, selectedLoader))
		{
			LocalizationService.Show(this, "Orbit Client поддерживает только Minecraft 26.2 + Fabric.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (OrbitClientCheck.IsChecked == true && (!Version.TryParse(text3, out Version result) || result < new Version(0, 19, 3)))
		{
			LocalizationService.Show(this, "Orbit Client требует Fabric Loader 0.19.3 или новее.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!int.TryParse(MinRamBox.Text, out var result2) || !int.TryParse(MaxRamBox.Text, out var result3) || result2 < 512 || result3 < result2 || result3 > 65536)
		{
			LocalizationService.Show(this, "Проверьте значения оперативной памяти.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		bool flag = !string.Equals(_originalGameVersion, text2, StringComparison.OrdinalIgnoreCase) || !string.Equals(_originalLoader, selectedLoader, StringComparison.OrdinalIgnoreCase) || !string.Equals(_originalLoaderVersion, text3, StringComparison.OrdinalIgnoreCase);
		_instance.Name = text;
		_instance.GameVersion = text2;
		_instance.Loader = selectedLoader;
		_instance.LoaderVersion = text3;
		_instance.OrbitClientEnabled = OrbitClientCheck.IsChecked == true;
		_instance.MinimumRamMb = result2;
		_instance.MaximumRamMb = result3;
		_instance.JavaPath = JavaPathBox.Text.Trim();
		string text4 = LaunchVersionBox.Text.Trim();
		_instance.LaunchVersionId = ((flag && text4 == _originalLaunchVersionId) ? string.Empty : text4);
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
