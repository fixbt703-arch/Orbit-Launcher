using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CmlLib.Core.Auth;
using Microsoft.Win32;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher;

public partial class MainWindow : Window
{
    private static readonly DependencyPropertyKey IsDefaultBackgroundPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsDefaultBackground), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));
    public static readonly DependencyProperty IsDefaultBackgroundProperty = IsDefaultBackgroundPropertyKey.DependencyProperty;
    public bool IsDefaultBackground => (bool)GetValue(IsDefaultBackgroundProperty);

	private readonly HttpClient _httpClient = LauncherNetwork.CreateClient();
	private readonly HttpClient _elyHttpClient = LauncherNetwork.CreateClient(redirects: false);

	private readonly InstanceRepository _instanceRepository = new InstanceRepository();

	private readonly SettingsService _settingsService = new SettingsService();

	private readonly AuthService _authService;
	private readonly ElyAccountService _elyAccounts;
	private readonly ElySkinService _elySkins;

	private readonly PlayerSkinService _playerSkinService;

	private readonly MinecraftSkinService _minecraftSkinService;

	private readonly SkinLibraryService _skinLibraryService = new SkinLibraryService();

	private readonly MinecraftService _minecraftService;

	private readonly ImageCacheService _installedIconLoader;

	private LauncherSettings _settings = new LauncherSettings();

	private CancellationTokenSource? _launchCancellation;

	private readonly List<InstalledMod> _allInstalledContent = new List<InstalledMod>();

	private CancellationTokenSource? _installedMetadataCancellation;

	private readonly ConcurrentDictionary<string, (long Length, DateTime Modified, string Name, BitmapSource? Icon)> _installedMetadataCache = new ConcurrentDictionary<string, (long, DateTime, string, BitmapSource?)>(StringComparer.OrdinalIgnoreCase);

	private bool _populatingSettings;

	private bool _skinPageLoaded;

	private bool _skinOperationInProgress;
    private bool _microsoftLoginInProgress;
    private string? _skinStatusMessage;
    private string? _skinStatusDetail;

	private bool _skinLibraryLoaded;
	private string? _accountHeadOwner;
	private int _accountHeadRevision;


	public ObservableCollection<GameInstance> Instances { get; } = new ObservableCollection<GameInstance>();

	public ObservableCollection<GameInstance> VisibleInstances { get; } = new ObservableCollection<GameInstance>();

	public ObservableCollection<InstalledMod> InstalledMods { get; } = new ObservableCollection<InstalledMod>();

	public ObservableCollection<DownloadItem> Downloads { get; } = new ObservableCollection<DownloadItem>();

	public ObservableCollection<SkinLibraryItem> SkinLibrary { get; } = new ObservableCollection<SkinLibraryItem>();

	private string SelectedContentKind => (ContentFolderCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mods";

	private string SelectedContentType => SelectedContentKind switch
	{
		"resourcepacks" => "Ресурспак", 
		"shaderpacks" => "Шейдер", 
		"downloads" => "Скачанный набор", 
		_ => "Мод", 
	};

	private string SelectedContentLabel => LocalizationService.Translate(SelectedContentKind switch
	{
		"resourcepacks" => "Ресурспаки", 
		"shaderpacks" => "Шейдеры", 
		"downloads" => "Скачанные наборы", 
		_ => "Моды", 
	});

	public MainWindow()
	{
		InitializeComponent();
		LocalizationService.Apply(this);
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.2 (Minecraft desktop launcher)");
		_httpClient.Timeout = TimeSpan.FromMinutes(10.0);
		_authService = new AuthService(_httpClient);
		_elyAccounts = new ElyAccountService(_elyHttpClient);
		_elySkins = new ElySkinService(_elyHttpClient);
		ElySkins.Initialize(_elyAccounts, _elySkins);
		ElySkins.AccountChanged += async (_, _) => await UpdateAccountCardAsync();
		_playerSkinService = new PlayerSkinService(_httpClient);
		_minecraftSkinService = new MinecraftSkinService(_httpClient);
		_minecraftService = new MinecraftService(_httpClient);
		_installedIconLoader = new ImageCacheService(_httpClient);
		base.DataContext = this;
		base.Loaded += MainWindow_Loaded;
		PreviewKeyDown += Launcher_KeyDown;
		SystemParameters.StaticPropertyChanged += AppearancePreferenceChanged;
		Closed += (_, _) => { SystemParameters.StaticPropertyChanged -= AppearancePreferenceChanged; ElySkins.Dispose(); _elyHttpClient.Dispose(); };
		InitializeUpdates();
	}

	private void LocalizedElement_Loaded(object sender, RoutedEventArgs e) => LocalizationService.Apply((DependencyObject)sender);

	private void AccountProfile_Click(object sender, RoutedEventArgs e) => SettingsNav.IsChecked = true;

	private void InstanceActions_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is not Button button) return;
		DependencyObject? node = button;
		while (node is not null && node is not Border { Name: "InstanceCard" }) node = VisualTreeHelper.GetParent(node);
		if (node is Border { ContextMenu: { } menu })
		{
			menu.DataContext = button.DataContext;
			menu.PlacementTarget = button;
			menu.IsOpen = true;
		}
	}

	private void Launcher_KeyDown(object sender, KeyEventArgs e)
	{
		if (Keyboard.Modifiers != ModifierKeys.Control) return;
		if (e.Key >= Key.D1 && e.Key <= Key.D4)
		{
			new[] { LibraryNav, ModsNav, SkinsNav, SettingsNav }[(int)e.Key - (int)Key.D1].IsChecked = true;
			e.Handled = true;
		}
		else if (e.Key == Key.F && (LibraryPage.IsVisible || ModsPage.IsVisible))
		{
			var search = LibraryPage.IsVisible ? InstanceSearchBox : ContentSearchBox;
			search.Focus(); search.SelectAll(); e.Handled = true;
		}
		else if (e.Key == Key.N) { CreateInstanceButton_Click(this, new RoutedEventArgs()); e.Handled = true; }
	}

	private void AppearancePreferenceChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SystemParameters.HighContrast) && IsLoaded)
			Dispatcher.Invoke(() => ApplyLauncherBackground(_settings.LauncherBackground, false));
	}

	private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		_settings = await _settingsService.LoadAsync();
		LocalizationService.SetLanguage(_settings.Language);
		LocalizationService.Apply(this);
		PopulateSettingsControls();
		await RefreshZapretCompatibilityAsync();
		await RefreshInstancesAsync();
		await _authService.TryRestoreSessionAsync(_settings);
		await _elyAccounts.RestoreAsync();
		await UpdateAccountCardAsync();
		LocalizationService.Apply(this);
		if (_instanceRepository.LoadWarnings.Count > 0)
			ShowStatus(LocalizationService.IsEnglish ? "Some instance definitions could not be read. Their files were kept."
				: "Не удалось прочитать часть сборок. Их файлы сохранены.");
		StartUpdateChecks();
	}

	private void PopulateSettingsControls()
	{
		_populatingSettings = true;
		try
		{
			DefaultMinRamBox.Text = _settings.DefaultMinimumRamMb.ToString();
			DefaultMaxRamBox.Text = _settings.DefaultMaximumRamMb.ToString();
			ShowSnapshotsCheck.IsChecked = _settings.ShowSnapshots;
			CloseOnLaunchCheck.IsChecked = _settings.CloseLauncherOnGameStart;
			OfflinePlayerNameBox.Text = (string.IsNullOrWhiteSpace(_settings.OfflinePlayerName) ? "OrbitPlayer" : _settings.OfflinePlayerName);
			OfflineModeCheck.IsChecked = _settings.OfflineModeEnabled;
			LicensedElySkinsCheck.IsChecked = _settings.ShowElySkinsForLicensedAccounts;
			OfflinePlayerNamePanel.IsEnabled = _settings.OfflineModeEnabled;
			DependencyDownloadModeCombo.SelectedValue = NormalizeDependencyMode(_settings.ModDependencyDownloadMode);
			AutoInstallFabricApiCheck.IsChecked = _settings.AutoInstallFabricApi;
			LanguageCombo.SelectedValue = NormalizeLanguage(_settings.Language);
			BackgroundDimmingSlider.Value = NormalizePercent(_settings.BackgroundDimmingPercent, 65);
			FrameTransparencySlider.Value = NormalizePercent(_settings.FrameTransparencyPercent, 70);
			UpdateAppearanceValueLabels();
			string text = NormalizeLauncherBackground(_settings.LauncherBackground);
			foreach (RadioButton item in BackgroundChoicesPanel.Children.OfType<RadioButton>())
			{
				item.IsChecked = string.Equals(item.Tag?.ToString(), text, StringComparison.Ordinal);
			}
			AppearanceTuningPanel.IsEnabled = text != "default";
			ApplyLauncherBackground(text, animate: false);
		}
		finally
		{
			_populatingSettings = false;
		}
	}

	private async Task RefreshInstancesAsync(string? selectId = null)
	{
		if (selectId == null)
		{
			selectId = (ModsInstanceCombo.SelectedItem as GameInstance)?.Id;
		}
		IReadOnlyList<GameInstance> obj = await _instanceRepository.GetAllAsync();
		Instances.Clear();
		foreach (GameInstance item in obj)
		{
			Instances.Add(item);
		}
		ApplyInstanceFilter();
		ModsInstanceCombo.SelectedItem = Instances.FirstOrDefault((GameInstance x) => x.Id == selectId) ?? Instances.FirstOrDefault();
		RefreshInstalledMods();
	}

	private void ApplyInstanceFilter()
	{
		if (RecentInstancePanel != null)
		{
			RecentInstancePanel.DataContext = Instances.OrderByDescending(item => item.LastPlayedAt ?? item.CreatedAt).FirstOrDefault();
			RecentInstancePanel.Visibility = Instances.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		}
		string query = InstanceSearchBox?.Text?.Trim() ?? string.Empty;
		VisibleInstances.Clear();
		foreach (GameInstance item in Instances.Where((GameInstance x) => string.IsNullOrEmpty(query) || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.GameVersion.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Loader.Contains(query, StringComparison.OrdinalIgnoreCase)))
		{
			VisibleInstances.Add(item);
		}
		if (InstanceCountText != null)
		{
			InstanceCountText.Text = (LocalizationService.IsEnglish ? $"{VisibleInstances.Count} of {Instances.Count}" : $"{VisibleInstances.Count} из {Instances.Count}");
		}
		if (EmptyInstancesPanel != null)
		{
			EmptyInstancesPanel.Visibility = ((VisibleInstances.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
            bool hasSearch = Instances.Count > 0;
            EmptyInstancesTitle.Text = LocalizationService.Translate(hasSearch ? "Ничего не найдено" : "Библиотека пуста");
            EmptyInstancesHint.Text = LocalizationService.Translate(hasSearch ? "Попробуйте другой запрос." : "Создайте сборку или импортируйте готовую.");
            EmptyCreateInstanceButton.Visibility = hasSearch ? Visibility.Collapsed : Visibility.Visible;
            ClearInstanceSearchButton.Visibility = hasSearch ? Visibility.Visible : Visibility.Collapsed;
		}
	}

	private void ClearInstanceSearch_Click(object sender, RoutedEventArgs e)
    {
        InstanceSearchBox.Clear();
        InstanceSearchBox.Focus();
    }

    private void InstanceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		ApplyInstanceFilter();
	}

	private async void CreateInstanceButton_Click(object sender, RoutedEventArgs e)
	{
		CreateInstanceDialog dialog = new CreateInstanceDialog(_httpClient, _settings)
		{
			Owner = this
		};
		if (dialog.ShowDialog() == true && dialog.Result != null)
		{
			GameInstance created = await _instanceRepository.CreateAsync(dialog.Result);
			if (!string.IsNullOrWhiteSpace(dialog.SelectedIconPath))
			{
				await SetInstanceIconAsync(created, dialog.SelectedIconPath);
			}
			string? fabricApiError = null;
			if (created.OrbitClientEnabled)
			{
				fabricApiError = await InstallOrbitClientAsync(created);
			}
			else if (_settings.AutoInstallFabricApi && string.Equals(created.Loader, "Fabric", StringComparison.OrdinalIgnoreCase))
			{
				fabricApiError = await InstallFabricApiAsync(created);
			}
            await RefreshInstancesAsync(created.Id);
			string text = (created.OrbitClientEnabled ? "Orbit Client" : "Fabric API");
			ShowStatus((fabricApiError == null) ? ("Сборка «" + created.Name + "» создана.") : $"Сборка «{created.Name}» создана без {text}.");
			if (fabricApiError != null)
			{
				LocalizationService.Show(this, "Сборка создана, но " + text + " не удалось установить автоматически. Установка будет повторена при следующем запуске сборки.\n\n" + fabricApiError, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
	}

	private async Task<string?> InstallOrbitClientAsync(GameInstance instance)
	{
		LaunchStatusBar.Visibility = Visibility.Visible;
		LaunchInstanceText.Text = (LocalizationService.IsEnglish ? ("Configuring “" + instance.Name + "”") : ("Настраиваем «" + instance.Name + "»"));
		LaunchStatusText.Text = LocalizationService.Translate("Установка Orbit Client…");
		LaunchProgressBar.Value = 0.0;
		Progress<LaunchProgress> progress = new Progress<LaunchProgress>(delegate(LaunchProgress value)
		{
			LaunchProgressBar.Value = Math.Clamp(value.Percent, 0.0, 100.0);
			LaunchStatusText.Text = LocalizationService.Translate(value.Status);
		});
		try
		{
			await new OrbitClientService(_httpClient).EnsureInstalledAsync(instance, progress);
			return null;
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
		finally
		{
			LaunchStatusBar.Visibility = Visibility.Collapsed;
		}
	}

	private async Task<string?> InstallFabricApiAsync(GameInstance instance)
	{
		LaunchStatusBar.Visibility = Visibility.Visible;
		LaunchInstanceText.Text = (LocalizationService.IsEnglish ? ("Configuring “" + instance.Name + "”") : ("Настраиваем «" + instance.Name + "»"));
		LaunchStatusText.Text = LocalizationService.Translate("Установка Fabric API…");
		LaunchProgressBar.Value = 0.0;
		Progress<double> progress = new Progress<double>(delegate(double value)
		{
			LaunchProgressBar.Value = Math.Clamp(value, 0.0, 100.0);
			LaunchStatusText.Text = $"Fabric API • {value:0}%";
		});
		try
		{
			ModrinthService modrinthService = new ModrinthService(_httpClient, _settings.ModrinthApiBase);
			ModSearchResult project = new ModSearchResult
			{
				ProjectId = "P7dR8mSH",
				SourceId = "modrinth",
				Title = "Fabric API",
				ProjectType = "mod"
			};
			await modrinthService.DownloadAsync(project, instance, instance.GameVersion, "fabric", "required", progress);
			LaunchProgressBar.Value = 100.0;
			LaunchStatusText.Text = "Fabric API установлен";
			return null;
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	private async void ImportButton_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Orbit / ZIP package (*.zip)|*.zip|Все файлы (*.*)|*.*"
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			GameInstance imported = await _instanceRepository.ImportAsync(openFileDialog.FileName);
			await RefreshInstancesAsync(imported.Id);
			ShowStatus("Сборка «" + imported.Name + "» импортирована.");
		}
		catch (Exception ex)
		{
			ShowError("Не удалось импортировать сборку", ex);
		}
	}

	private async void LaunchCardButton_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is GameInstance instance)
		{
			await LaunchInstanceAsync(instance);
		}
	}

	private async void InstanceCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is GameInstance instance)
		{
			await LaunchInstanceAsync(instance);
		}
	}

	private async void ContextLaunch_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? gameInstance = ContextInstance(sender);
		if (gameInstance != null)
		{
			await LaunchInstanceAsync(gameInstance);
		}
	}

	private async Task LaunchInstanceAsync(GameInstance instance)
	{
		if (!_settings.OfflineModeEnabled && !_authService.IsSignedIn)
		{
			LocalizationService.Show(this, "Для запуска игры войдите в Microsoft-аккаунт, которому принадлежит Minecraft Java Edition. Библиотекой и настройками можно пользоваться без входа.", "Требуется аккаунт", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			SettingsNav.IsChecked = true;
			return;
		}
		if (_launchCancellation != null)
		{
			LocalizationService.Show(this, "Другая сборка уже готовится к запуску.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		DownloadItem download = new DownloadItem
		{
			Title = instance.Name,
			Subtitle = instance.Subtitle,
			Status = LocalizationService.Translate("Подготовка")
		};
		Downloads.Insert(0, download);
		_launchCancellation = new CancellationTokenSource();
		LaunchStatusBar.Visibility = Visibility.Visible;
		LaunchInstanceText.Text = (LocalizationService.IsEnglish ? ("Launching: " + instance.Name) : ("Запуск: " + instance.Name));
		LaunchStatusText.Text = LocalizationService.Translate("Подготовка…");
		LaunchProgressBar.Value = 0.0;
		Progress<LaunchProgress> progress = new Progress<LaunchProgress>(delegate(LaunchProgress value)
		{
			LaunchProgressBar.Value = Math.Clamp(value.Percent, 0.0, 100.0);
			LaunchStatusText.Text = LocalizationService.Translate(value.Status);
			download.Progress = Math.Clamp(value.Percent, 0.0, 100.0);
			download.Status = LocalizationService.Translate(value.Status);
		});
		try
		{
			bool useEly = _settings.OfflineModeEnabled && _elyAccounts.IsSignedIn;
			bool showElySkins = _settings.OfflineModeEnabled || _settings.ShowElySkinsForLicensedAccounts;
			MSession mSession = useEly ? await _elyAccounts.GetRequiredSessionAsync(_launchCancellation.Token) :
				(!_settings.OfflineModeEnabled ? await _authService.GetRequiredSessionAsync(_settings) : MSession.CreateOfflineSession(_settings.OfflinePlayerName));
			MSession session = mSession;
			await UpdateAccountCardAsync();
			LaunchResult result = await _minecraftService.InstallAndLaunchAsync(instance, session, _settings, progress, null, _launchCancellation.Token, useEly, showElySkins);
			instance.LaunchVersionId = result.VersionId;
			instance.LastPlayedAt = DateTime.UtcNow;
			await _instanceRepository.SaveAsync(instance);
			download.Progress = 100.0;
			download.Status = (LocalizationService.IsEnglish ? $"Launched, PID {result.ProcessId}" : $"Запущено, PID {result.ProcessId}");
			LaunchStatusBar.Visibility = Visibility.Collapsed;
			if (_settings.CloseLauncherOnGameStart)
			{
				base.WindowState = WindowState.Minimized;
			}
			await RefreshInstancesAsync(instance.Id);
		}
		catch (OperationCanceledException)
		{
			download.Status = LocalizationService.Translate("Отменено");
			LaunchStatusText.Text = LocalizationService.Translate("Запуск отменён");
		}
		catch (Exception ex2)
		{
			download.Status = LocalizationService.Translate("Ошибка");
			if (ex2 is ElySessionExpiredException) await UpdateAccountCardAsync();
			ShowError("Не удалось запустить Minecraft", ex2);
		}
		finally
		{
			_launchCancellation.Dispose();
			_launchCancellation = null;
			LaunchStatusBar.Visibility = Visibility.Collapsed;
		}
	}

	private void CancelLaunchButton_Click(object sender, RoutedEventArgs e)
	{
		_launchCancellation?.Cancel();
	}

	private async void ContextEdit_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? instance = ContextInstance(sender);
		if (instance != null && new EditInstanceDialog(_httpClient, _settings, instance)
		{
			Owner = this
		}.ShowDialog() == true)
		{
			await _instanceRepository.SaveAsync(instance);
			await RefreshInstancesAsync(instance.Id);
		}
	}

	private async void ContextRename_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? instance = ContextInstance(sender);
		if (instance != null)
		{
			TextPromptDialog textPromptDialog = new TextPromptDialog("Переименовать сборку", "Новое название", instance.Name)
			{
				Owner = this
			};
			if (textPromptDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textPromptDialog.ResultText))
			{
				instance.Name = textPromptDialog.ResultText.Trim();
				await _instanceRepository.SaveAsync(instance);
				await RefreshInstancesAsync(instance.Id);
			}
		}
	}

	private async void ContextIcon_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? instance = ContextInstance(sender);
		if (instance != null)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
			};
			if (openFileDialog.ShowDialog(this) == true)
			{
				await SetInstanceIconAsync(instance, openFileDialog.FileName);
				await RefreshInstancesAsync(instance.Id);
			}
		}
	}

	private async void ContextMods_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? gameInstance = ContextInstance(sender);
		if (gameInstance != null)
		{
			await OpenModBrowserAsync(gameInstance);
		}
	}

	private async void ContextDuplicate_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? gameInstance = ContextInstance(sender);
		if (gameInstance == null)
		{
			return;
		}
		try
		{
			await RefreshInstancesAsync((await _instanceRepository.DuplicateAsync(gameInstance)).Id);
			ShowStatus("Копия сборки создана.");
		}
		catch (Exception ex)
		{
			ShowError("Не удалось дублировать сборку", ex);
		}
	}

	private async void ContextExport_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? gameInstance = ContextInstance(sender);
		if (gameInstance == null)
		{
			return;
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "ZIP-архив (*.zip)|*.zip",
			FileName = SanitizeFileName(gameInstance.Name) + ".zip"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			await _instanceRepository.ExportAsync(gameInstance, saveFileDialog.FileName);
			ShowStatus("Сборка экспортирована.");
		}
		catch (Exception ex)
		{
			ShowError("Не удалось экспортировать сборку", ex);
		}
	}

	private void ContextFolder_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? gameInstance = ContextInstance(sender);
		if (gameInstance != null)
		{
			OpenFolder(gameInstance.DirectoryPath);
		}
	}

    private void ContextLaunchLog_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance) return;
        var directory = System.IO.Path.Combine(instance.DirectoryPath, ".orbit", "logs");
        var latest = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "launch-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (latest is null) { ShowStatus("Журнал появится после первого запуска этой сборки."); return; }
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/select,"); start.ArgumentList.Add(latest); Process.Start(start);
    }

	private async void ContextDelete_Click(object sender, RoutedEventArgs e)
	{
		GameInstance? instance = ContextInstance(sender);
		if (instance == null || LocalizationService.Show(this, "Удалить сборку «" + instance.Name + "» вместе с её мирами, модами и настройками?\n\nЭто действие нельзя отменить.", "Удаление сборки", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			_ = 1;
			try
			{
				VisibleInstances.Remove(instance);
				Instances.Remove(instance);
				InstalledMods.Clear();
				await base.Dispatcher.InvokeAsync(delegate
				{
				}, DispatcherPriority.Render);
				GC.Collect();
				GC.WaitForPendingFinalizers();
				await _instanceRepository.DeleteAsync(instance);
				ShowStatus("Сборка «" + instance.Name + "» удалена.");
			}
			catch (Exception ex)
			{
				ShowError("Не удалось удалить сборку", ex);
			}
		}
		finally
		{
			await RefreshInstancesAsync();
		}
	}

	private static GameInstance? ContextInstance(object sender)
	{
		return (sender as FrameworkElement)?.DataContext as GameInstance;
	}

	private async void Navigation_Checked(object sender, RoutedEventArgs e)
	{
		if (base.IsLoaded && sender is RadioButton { Tag: var tag })
		{
			string? text = tag?.ToString();
			LibraryPage.Visibility = ((!(text == "Library")) ? Visibility.Collapsed : Visibility.Visible);
			ModsPage.Visibility = ((!(text == "Mods")) ? Visibility.Collapsed : Visibility.Visible);
			SkinsPage.Visibility = ((!(text == "Skins")) ? Visibility.Collapsed : Visibility.Visible);
			SettingsPage.Visibility = ((!(text == "Settings")) ? Visibility.Collapsed : Visibility.Visible);
			var selectedPage = text switch { "Mods" => ModsPage, "Skins" => SkinsPage, "Settings" => SettingsPage, _ => LibraryPage };
			InterfaceMotion.Reveal(selectedPage);
			if (text == "Mods")
			{
				RefreshInstalledMods();
			}
			else if (text == "Skins")
			{
				await RefreshSkinPageAsync(!_skinPageLoaded);
			}
			else if (text == "Settings" && !_zapretRepairInProgress)
			{
				await RefreshZapretCompatibilityAsync();
			}
		}
	}

	private void ModsInstanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		RefreshInstalledMods();
	}

	private void ContentFolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		RefreshInstalledMods();
	}

	private void ContentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		ApplyInstalledContentView();
	}

	private void ContentFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ApplyInstalledContentView();
	}

	private string GetSelectedContentDirectory(GameInstance instance)
	{
		return SelectedContentKind switch
		{
			"resourcepacks" => System.IO.Path.Combine(instance.DirectoryPath, "resourcepacks"), 
			"shaderpacks" => System.IO.Path.Combine(instance.DirectoryPath, "shaderpacks"), 
			"downloads" => System.IO.Path.Combine(instance.DirectoryPath, ".orbit", "downloads"), 
			_ => instance.ModsPath, 
		};
	}

	private void RefreshInstalledMods()
	{
		_installedMetadataCancellation?.Cancel();
		_installedMetadataCancellation?.Dispose();
		_installedMetadataCancellation = new CancellationTokenSource();
		_allInstalledContent.Clear();
		InstalledMods.Clear();
		if (!(ModsInstanceCombo?.SelectedItem is GameInstance gameInstance))
		{
			if (ModsSummaryText != null)
			{
				ModsSummaryText.Text = LocalizationService.Translate("Выберите сборку");
			}
			UpdateContentStatistics(null);
			UpdateContentEmptyState();
			return;
		}
		UpdateContentStatistics(gameInstance);
		if (EnableAllContentButton != null && DisableAllContentButton != null)
		{
			Visibility visibility = ((SelectedContentKind == "downloads") ? Visibility.Collapsed : Visibility.Visible);
			EnableAllContentButton.Visibility = visibility;
			DisableAllContentButton.Visibility = visibility;
		}
		string selectedContentDirectory = GetSelectedContentDirectory(gameInstance);
		IReadOnlyList<InstalledContentRecord> readOnlyList = new InstalledContentRegistry().Load(gameInstance);
		Directory.CreateDirectory(selectedContentDirectory);
		SearchOption searchOption = ((SelectedContentKind == "downloads") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
		foreach (string file in (from path in Directory.EnumerateFiles(selectedContentDirectory, "*", searchOption)
			where path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
			select path).OrderBy(System.IO.Path.GetFileName))
		{
			FileInfo fileInfo = new FileInfo(file);
			string relative = System.IO.Path.GetRelativePath(gameInstance.DirectoryPath, file);
			InstalledContentRecord? installedContentRecord = readOnlyList.FirstOrDefault((InstalledContentRecord item) => string.Equals(item.RelativePath, relative, StringComparison.OrdinalIgnoreCase) || (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && string.Equals(item.RelativePath + ".disabled", relative, StringComparison.OrdinalIgnoreCase)));
			_allInstalledContent.Add(new InstalledMod
			{
				Name = ((!string.IsNullOrWhiteSpace(installedContentRecord?.Title)) ? installedContentRecord.Title : fileInfo.Name.Replace(".disabled", string.Empty, StringComparison.OrdinalIgnoreCase)),
				FileName = fileInfo.Name,
				FullPath = file,
				IsEnabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
				SizeBytes = fileInfo.Length,
				TypeText = SelectedContentType,
				ModifiedAt = fileInfo.LastWriteTime,
				FolderText = System.IO.Path.GetRelativePath(selectedContentDirectory, fileInfo.DirectoryName ?? selectedContentDirectory),
				CanToggle = (SelectedContentKind != "downloads")
			});
		}
		ApplyInstalledContentView();
		_ = PopulateInstalledMetadataAsync(gameInstance, readOnlyList, _installedMetadataCancellation.Token);
	}

	private async Task PopulateInstalledMetadataAsync(GameInstance instance, IReadOnlyList<InstalledContentRecord> registry, CancellationToken token)
	{
		SemaphoreSlim limiter = new SemaphoreSlim(3, 3);
		try
		{
			IEnumerable<Task> tasks = ((IEnumerable<InstalledMod>)_allInstalledContent.ToArray()).Select((Func<InstalledMod, Task>)async delegate(InstalledMod item)
			{
				await limiter.WaitAsync(token);
				try
				{
					(string Name, BitmapSource? Icon) metadata;
					if (_installedMetadataCache.TryGetValue(item.FullPath, out var value) && value.Item1 == item.SizeBytes && value.Item2 == item.ModifiedAt)
					{
						metadata = (Name: value.Item3, Icon: value.Item4);
					}
					else
					{
						metadata = await InstalledModMetadataService.ReadAsync(item.FullPath, token);
						if (!token.IsCancellationRequested)
						{
							_installedMetadataCache[item.FullPath] = (item.SizeBytes, item.ModifiedAt, metadata.Name, metadata.Icon);
						}
					}
					if (!token.IsCancellationRequested)
					{
						string relative = System.IO.Path.GetRelativePath(instance.DirectoryPath, item.FullPath);
						InstalledContentRecord? record = registry.FirstOrDefault((InstalledContentRecord installedContentRecord) => string.Equals(installedContentRecord.RelativePath, relative, StringComparison.OrdinalIgnoreCase) || (item.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && string.Equals(installedContentRecord.RelativePath + ".disabled", relative, StringComparison.OrdinalIgnoreCase)));
						await base.Dispatcher.InvokeAsync(delegate
						{
							if (!string.IsNullOrWhiteSpace(record?.Title))
							{
								item.Name = record.Title;
							}
							else if (!string.IsNullOrWhiteSpace(metadata.Name))
							{
								item.Name = metadata.Name;
							}
							item.Icon = metadata.Icon;
							if (item.Icon == null && item.FileName.StartsWith("orbit-client-", StringComparison.OrdinalIgnoreCase))
							{
								item.Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/OrbitLauncher.png"));
							}
						}, DispatcherPriority.Background, token);
						if (metadata.Icon == null && !string.IsNullOrWhiteSpace(record?.IconUrl))
						{
							BitmapSource? icon = await _installedIconLoader.GetAsync(record.IconUrl, 96, token);
							if (!token.IsCancellationRequested && icon != null)
							{
								await base.Dispatcher.InvokeAsync(() => item.Icon = icon, DispatcherPriority.Background, token);
							}
						}
					}
				}
				catch (OperationCanceledException)
				{
				}
				finally
				{
					limiter.Release();
				}
			});
			try
			{
				await Task.WhenAll(tasks);
			}
			catch (OperationCanceledException)
			{
			}
			if (!token.IsCancellationRequested)
			{
				ApplyInstalledContentView();
			}
		}
		finally
		{
			if (limiter != null)
			{
				((IDisposable)limiter).Dispose();
			}
		}
	}

	private void ApplyInstalledContentView()
	{
		if (InstalledMods == null)
		{
			return;
		}
		string query = ContentSearchBox?.Text?.Trim() ?? string.Empty;
		string text = (ContentStateCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
		string text2 = (ContentSortCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "name";
		IEnumerable<InstalledMod> enumerable = _allInstalledContent;
		if (query.Length > 0)
		{
			enumerable = enumerable.Where((InstalledMod item) => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
		}
		IEnumerable<InstalledMod> enumerable2 = ((text == "enabled") ? enumerable.Where((InstalledMod item) => item.IsEnabled) : ((!(text == "disabled")) ? enumerable : enumerable.Where((InstalledMod item) => !item.IsEnabled)));
		enumerable = enumerable2;
		IOrderedEnumerable<InstalledMod> orderedEnumerable = ((text2 == "size") ? (from item in enumerable
			orderby item.SizeBytes descending, item.Name
			select item) : ((!(text2 == "date")) ? enumerable.OrderBy((InstalledMod item) => item.Name) : (from item in enumerable
			orderby item.ModifiedAt descending, item.Name
			select item)));
		enumerable = orderedEnumerable;
		InstalledMods.Clear();
		foreach (InstalledMod item in enumerable)
		{
			InstalledMods.Add(item);
		}
		if (ModsSummaryText != null && ModsInstanceCombo?.SelectedItem is GameInstance gameInstance)
		{
			ModsSummaryText.Text = (LocalizationService.IsEnglish ? $"{gameInstance.Name}  •  {SelectedContentLabel}  •  showing {InstalledMods.Count} of {_allInstalledContent.Count}" : $"{gameInstance.Name}  •  {SelectedContentLabel}  •  показано {InstalledMods.Count} из {_allInstalledContent.Count}");
		}
		UpdateContentEmptyState();
	}

	private void UpdateContentEmptyState()
	{
		if (ModsEmptyPanel != null && ModsEmptyTitle != null)
		{
			ModsEmptyPanel.Visibility = ((InstalledMods.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
			ModsEmptyTitle.Text = LocalizationService.Translate((_allInstalledContent.Count == 0) ? "Здесь пока пусто" : "По фильтрам ничего не найдено");
		}
	}

	private void UpdateContentStatistics(GameInstance? instance)
	{
		if (ModsCountText != null && ResourcePacksCountText != null && ShadersCountText != null && DownloadedCountText != null)
		{
			ModsCountText.Text = ((instance == null) ? "0" : CountContentFiles(instance.ModsPath, recursive: false).ToString());
			ResourcePacksCountText.Text = ((instance == null) ? "0" : CountContentFiles(System.IO.Path.Combine(instance.DirectoryPath, "resourcepacks"), recursive: false).ToString());
			ShadersCountText.Text = ((instance == null) ? "0" : CountContentFiles(System.IO.Path.Combine(instance.DirectoryPath, "shaderpacks"), recursive: false).ToString());
			DownloadedCountText.Text = ((instance == null) ? "0" : CountContentFiles(System.IO.Path.Combine(instance.DirectoryPath, ".orbit", "downloads"), recursive: true).ToString());
		}
	}

	private static int CountContentFiles(string directory, bool recursive)
	{
		if (!Directory.Exists(directory))
		{
			return 0;
		}
		try
		{
			return Directory.EnumerateFiles(directory, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Count(IsSupportedContentFile);
		}
		catch
		{
			return 0;
		}
	}

	private static bool IsSupportedContentFile(string path)
	{
		if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
		{
			return path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private async void BrowseModsButton_Click(object sender, RoutedEventArgs e)
	{
		if (ModsInstanceCombo.SelectedItem is GameInstance instance)
		{
			await OpenModBrowserAsync(instance);
		}
		else
		{
			LocalizationService.Show(this, "Сначала выберите сборку.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	private async void RepairModsButton_Click(object sender, RoutedEventArgs e)
	{
		if (!(ModsInstanceCombo.SelectedItem is GameInstance gameInstance))
		{
			LocalizationService.Show(this, "Сначала выберите сборку.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		RepairModsWindow repairModsWindow = new RepairModsWindow(gameInstance, _httpClient, _settings.ModrinthApiBase, _settings.CurseForgeApiBase, _settings.CurseForgeApiKey);
		repairModsWindow.Owner = this;
		repairModsWindow.ShowDialog();
		RefreshInstalledMods();
		await RefreshInstancesAsync(gameInstance.Id);
	}

	private async Task OpenModBrowserAsync(GameInstance instance)
	{
		ModBrowserWindow modBrowserWindow = new ModBrowserWindow(_httpClient, instance, Downloads, _settings.ModrinthApiBase, _settings.ModDependencyDownloadMode, _settings.CurseForgeApiBase, _settings.CurseForgeApiKey);
		modBrowserWindow.Owner = this;
		modBrowserWindow.ShowDialog();
		RefreshInstalledMods();
		await RefreshInstancesAsync(instance.Id);
	}

	private void OpenModsFolderButton_Click(object sender, RoutedEventArgs e)
	{
		if (ModsInstanceCombo.SelectedItem is GameInstance instance)
		{
			string selectedContentDirectory = GetSelectedContentDirectory(instance);
			Directory.CreateDirectory(selectedContentDirectory);
			OpenFolder(selectedContentDirectory);
		}
	}

	private void RefreshModsButton_Click(object sender, RoutedEventArgs e)
	{
		RefreshInstalledMods();
	}

	private void EnableAllContent_Click(object sender, RoutedEventArgs e)
	{
		SetAllContentEnabled(enabled: true);
	}

	private void DisableAllContent_Click(object sender, RoutedEventArgs e)
	{
		SetAllContentEnabled(enabled: false);
	}

	private void SetAllContentEnabled(bool enabled)
	{
		int num = 0;
		foreach (InstalledMod item in _allInstalledContent.Where((InstalledMod item) => item.CanToggle && item.IsEnabled != enabled).ToList())
		{
			try
			{
				string text;
				if (!enabled)
				{
					text = item.FullPath + ".disabled";
				}
				else
				{
					string fullPath = item.FullPath;
					int length = ".disabled".Length;
					text = fullPath.Substring(0, fullPath.Length - length);
				}
				string text2 = text;
				if (File.Exists(text2))
				{
					num++;
				}
				else
				{
					File.Move(item.FullPath, text2);
				}
			}
			catch
			{
				num++;
			}
		}
		RefreshInstalledMods();
		if (num > 0)
		{
			LocalizationService.Show(this, $"Не удалось изменить состояние файлов: {num}.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void ToggleModButton_Click(object sender, RoutedEventArgs e)
	{
		if (!((sender as FrameworkElement)?.Tag is InstalledMod installedMod))
		{
			return;
		}
		try
		{
			string text;
			if (!installedMod.IsEnabled)
			{
				string fullPath = installedMod.FullPath;
				int length = ".disabled".Length;
				text = fullPath.Substring(0, fullPath.Length - length);
			}
			else
			{
				text = installedMod.FullPath + ".disabled";
			}
			string destFileName = text;
			File.Move(installedMod.FullPath, destFileName, overwrite: false);
			RefreshInstalledMods();
		}
		catch (Exception ex)
		{
			ShowError("Не удалось изменить состояние файла", ex);
		}
	}

	private void DeleteModButton_Click(object sender, RoutedEventArgs e)
	{
		if (!((sender as FrameworkElement)?.Tag is InstalledMod installedMod) || LocalizationService.Show(this, "Удалить файл «" + installedMod.Name + "»?", "Удаление контента", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			File.Delete(installedMod.FullPath);
			RefreshInstalledMods();
		}
		catch (Exception ex)
		{
			ShowError("Не удалось удалить файл", ex);
		}
	}

	private void ShowContentFileButton_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is InstalledMod installedMod && File.Exists(installedMod.FullPath))
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + installedMod.FullPath + "\"")
			{
				UseShellExecute = true
			});
		}
	}

	private void ModsPage_DragOver(object sender, DragEventArgs e)
	{
		if (!(ModsInstanceCombo?.SelectedItem is GameInstance) || !e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effects = DragDropEffects.None;
			e.Handled = true;
		}
		else
		{
			string[] source = (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>();
			e.Effects = (source.Any(IsAllowedDropFile) ? DragDropEffects.Copy : DragDropEffects.None);
			e.Handled = true;
		}
	}

	private void ModsPage_Drop(object sender, DragEventArgs e)
	{
		if (!(ModsInstanceCombo?.SelectedItem is GameInstance instance) || !(e.Data.GetData(DataFormats.FileDrop) is string[] source))
		{
			return;
		}
		string selectedContentDirectory = GetSelectedContentDirectory(instance);
		Directory.CreateDirectory(selectedContentDirectory);
		int num = 0;
		int num2 = 0;
		foreach (string item in source.Where(File.Exists))
		{
			if (!IsAllowedDropFile(item))
			{
				num2++;
				continue;
			}
			try
			{
				string destFileName = UniqueDestinationPath(selectedContentDirectory, System.IO.Path.GetFileName(item));
				File.Copy(item, destFileName, overwrite: false);
				num++;
			}
			catch
			{
				num2++;
			}
		}
		RefreshInstalledMods();
		if (num > 0 || num2 > 0)
		{
			LocalizationService.Show(this, $"Добавлено файлов: {num}. Пропущено: {num2}.", "Orbit Launcher", MessageBoxButton.OK, (num2 > 0) ? MessageBoxImage.Exclamation : MessageBoxImage.Asterisk);
		}
	}

	private bool IsAllowedDropFile(string path)
	{
		switch (SelectedContentKind)
		{
		case "mods":
			return path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
		case "resourcepacks":
		case "shaderpacks":
			return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase);
		case "downloads":
			return path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
		default:
			return false;
		}
	}

	private static string UniqueDestinationPath(string directory, string fileName)
	{
		string text = System.IO.Path.Combine(directory, fileName);
		if (!File.Exists(text))
		{
			return text;
		}
		string extension = System.IO.Path.GetExtension(fileName);
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);
		int num = 2;
		while (true)
		{
			text = System.IO.Path.Combine(directory, $"{fileNameWithoutExtension} ({num}){extension}");
			if (!File.Exists(text))
			{
				break;
			}
			num++;
		}
		return text;
	}

	private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
	{
		if (TryReadSettingsControls())
		{
			LocalizationService.SetLanguage(_settings.Language);
			await _settingsService.SaveAsync(_settings);
			LocalizationService.Apply(this);
			await RefreshInstancesAsync();
			await UpdateAccountCardAsync();
			ShowStatus(LocalizationService.Translate("Настройки сохранены."));
		}
	}

	private async void MicrosoftLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_microsoftLoginInProgress) return;
		if (string.IsNullOrWhiteSpace(_settings.MicrosoftClientId))
		{
			LocalizationService.Show(this, "Microsoft Client ID не задан в конфигурации этой сборки. Обратитесь к автору лаунчера.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
        _microsoftLoginInProgress = true; SkinMicrosoftLoginButton.IsEnabled = false;
		GateLoginButton.IsEnabled = false;
		SettingsLoginButton.IsEnabled = false;
		GateLoginButton.Content = LocalizationService.Translate("Открываем Microsoft…");
		SettingsLoginButton.Content = LocalizationService.Translate("Открываем Microsoft…");
		try
		{
			await _authService.LoginInteractivelyAsync(_settings);
			await UpdateAccountCardAsync();
			_skinPageLoaded = false;
			if (SkinsNav.IsChecked == true)
			{
				await RefreshSkinPageAsync();
			}
			ShowStatus("Вход в Microsoft выполнен безопасно.");
		}
		catch (Exception ex)
		{
			ShowError("Не удалось войти в Microsoft", ex);
		}
		finally
		{
            _microsoftLoginInProgress = false; SkinMicrosoftLoginButton.IsEnabled = true;
			GateLoginButton.IsEnabled = true;
			SettingsLoginButton.IsEnabled = true;
			GateLoginButton.Content = LocalizationService.Translate("Продолжить с Microsoft");
			SettingsLoginButton.Content = LocalizationService.Translate("Войти с Microsoft");
		}
	}

	private async void SignOutButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await _authService.SignOutAsync(_settings);
		}
		catch (Exception)
		{
		}
		await UpdateAccountCardAsync();
		_skinPageLoaded = false;
		UpdateSkinAccountState();
	}

	private bool TryReadSettingsControls()
	{
		if (!int.TryParse(DefaultMinRamBox.Text, out var result) || !int.TryParse(DefaultMaxRamBox.Text, out var result2) || result < 512 || result2 < result || result2 > 65536)
		{
			LocalizationService.Show(this, "Проверьте объём памяти: минимум от 512 МБ, максимум не меньше минимума.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		try
		{
			string text = OfflinePlayerNameBox.Text.Trim();
			if (OfflineModeCheck.IsChecked == true && !Regex.IsMatch(text, "^[A-Za-z0-9_]{3,16}$"))
			{
				throw new InvalidOperationException("Ник игрока должен содержать от 3 до 16 латинских букв, цифр или знаков подчёркивания.");
			}
			_settings.DefaultMinimumRamMb = result;
			_settings.DefaultMaximumRamMb = result2;
			_settings.ShowSnapshots = ShowSnapshotsCheck.IsChecked == true;
			_settings.CloseLauncherOnGameStart = CloseOnLaunchCheck.IsChecked == true;
			_settings.OfflineModeEnabled = OfflineModeCheck.IsChecked == true;
			_settings.ShowElySkinsForLicensedAccounts = LicensedElySkinsCheck.IsChecked == true;
			_settings.OfflinePlayerName = (string.IsNullOrWhiteSpace(text) ? "OrbitPlayer" : text);
			_settings.ModDependencyDownloadMode = NormalizeDependencyMode((DependencyDownloadModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
			_settings.AutoInstallFabricApi = AutoInstallFabricApiCheck.IsChecked == true;
			_settings.Language = NormalizeLanguage((LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
			_settings.LauncherBackground = NormalizeLauncherBackground(BackgroundChoicesPanel.Children.OfType<RadioButton>().FirstOrDefault((RadioButton choice) => choice.IsChecked == true)?.Tag?.ToString());
			_settings.BackgroundDimmingPercent = NormalizePercent((int)Math.Round(BackgroundDimmingSlider.Value), 65);
			_settings.FrameTransparencyPercent = NormalizePercent((int)Math.Round(FrameTransparencySlider.Value), 70);
		}
		catch (InvalidOperationException ex)
		{
			LocalizationService.Show(this, ex.Message, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		return true;
	}

	private static string NormalizeDependencyMode(string? value)
	{
		if ((!(value == "all") && !(value == "none")) || 1 == 0)
		{
			return "required";
		}
		return value;
	}

	private static string NormalizeLanguage(string? value)
	{
		if (!string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
		{
			return "ru";
		}
		return "en";
	}

    private static string NormalizeLauncherBackground(string? value) => value?.ToLowerInvariant() switch
    {
        "space" or "water" or "fight" or "ghast" or "forest" or "nether" or "village" or "cave" => value.ToLowerInvariant(),
        _ => "default"
    };

	private static int NormalizePercent(int value, int fallback)
	{
		if (value < 0 || value > 90)
		{
			return fallback;
		}
		return value;
	}

	private void BackgroundChoice_Checked(object sender, RoutedEventArgs e)
	{
		if (!_populatingSettings && sender is RadioButton { Tag: var tag })
		{
			string text = NormalizeLauncherBackground(tag?.ToString());
			AppearanceTuningPanel.IsEnabled = text != "default";
			ApplyLauncherBackground(text, animate: true);
		}
	}

	private void AppearanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (BackgroundDimmingValueText != null && FrameTransparencyValueText != null)
		{
			UpdateAppearanceValueLabels();
			if (!_populatingSettings && base.IsLoaded && NormalizeLauncherBackground(BackgroundChoicesPanel.Children.OfType<RadioButton>().FirstOrDefault((RadioButton choice) => choice.IsChecked == true)?.Tag?.ToString()) != "default")
			{
				SetCustomBackgroundAppearance(enabled: true);
			}
		}
	}

	private void UpdateAppearanceValueLabels()
	{
		BackgroundDimmingValueText.Text = $"{Math.Round(BackgroundDimmingSlider.Value):0}%";
		FrameTransparencyValueText.Text = $"{Math.Round(FrameTransparencySlider.Value):0}%";
	}

	private void ApplyLauncherBackground(string? value, bool animate)
	{
		if (SystemParameters.HighContrast) value = "default";
		string text = NormalizeLauncherBackground(value);
		if (text == "default")
		{
			SetCustomBackgroundAppearance(enabled: false);
			LauncherBackgroundImage.BeginAnimation(UIElement.OpacityProperty, null);
			LauncherBackgroundImage.Source = null;
			LauncherBackgroundImage.Visibility = Visibility.Collapsed;
			LauncherBackgroundShade.Visibility = Visibility.Collapsed;
			return;
		}
		string text2 = text switch
		{
			"village" => "Village.png", 
			"water" => "Water.png", 
			"ghast" => "Ghast.png", 
			"forest" => "Forest.png", 
			"cave" => "Cave.png", 
			"fight" => "Fight.png", 
			"nether" => "Nether.png", 
			"space" => "Space.png", 
			_ => throw new ArgumentOutOfRangeException("value"), 
		};
		try
		{
			SetCustomBackgroundAppearance(enabled: true);
			int decodePixelWidth = Math.Clamp((int)Math.Ceiling(Math.Max(base.ActualWidth, 1280.0) * 1.35), 1728, 2560);
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.UriSource = new Uri("pack://application:,,,/OrbitLauncher;component/Assets/" + text2, UriKind.Absolute);
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bitmapImage.DecodePixelWidth = decodePixelWidth;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			LauncherBackgroundImage.BeginAnimation(UIElement.OpacityProperty, null);
			LauncherBackgroundImage.Source = bitmapImage;
			LauncherBackgroundImage.Visibility = Visibility.Visible;
			LauncherBackgroundShade.Visibility = Visibility.Visible;
			LauncherBackgroundImage.Opacity = 1.0;
			if (animate && InterfaceMotion.CanAnimate)
			{
				LauncherBackgroundImage.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				});
			}
		}
		catch
		{
			SetCustomBackgroundAppearance(enabled: false);
			LauncherBackgroundImage.Source = null;
			LauncherBackgroundImage.Visibility = Visibility.Collapsed;
			LauncherBackgroundShade.Visibility = Visibility.Collapsed;
		}
	}

	private void SetCustomBackgroundAppearance(bool enabled)
	{
		enabled &= !SystemParameters.HighContrast;
        bool standard = !enabled && !SystemParameters.HighContrast;
        SetValue(IsDefaultBackgroundPropertyKey, standard);
        DefaultSpaceDecoration.Visibility = standard ? Visibility.Visible : Visibility.Collapsed;
		if (!enabled)
		{
			foreach (var key in new[] { "CardBrush", "CardHoverBrush", "SubtleCardBrush", "TitleBarFrameBrush", "NavigationFrameBrush" }) Resources.Remove(key);
			return;
		}
		double opacity = (double)NormalizePercent((int)Math.Round(BackgroundDimmingSlider.Value), 65) / 100.0;
		double num = (double)NormalizePercent((int)Math.Round(FrameTransparencySlider.Value), 70) / 100.0;
		double num2 = 1.0 - num;
		double opacity2 = Math.Min(1.0, num2 + 0.1);
		LauncherBackgroundShade.Background = CreateSurfaceBrush("BackgroundBrush", opacity);
		base.Resources["CardBrush"] = CreateSurfaceBrush("CardBrush", num2);
		base.Resources["CardHoverBrush"] = CreateSurfaceBrush("CardHoverBrush", opacity2);
		base.Resources["SubtleCardBrush"] = CreateSurfaceBrush("PanelBrush", num2);
		base.Resources["TitleBarFrameBrush"] = CreateSurfaceBrush("BackgroundBrush", num2);
		base.Resources["NavigationFrameBrush"] = CreateSurfaceBrush("BackgroundBrush", num2);
	}

	private static SolidColorBrush CreateSurfaceBrush(string resource, double opacity)
    {
        var source = (SolidColorBrush)Application.Current.FindResource(resource);
        var color = source.Color;
        color.A = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateFrozenBrush(string value)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static SolidColorBrush CreateFrozenBrush(string value, double opacity)
	{
		Color color = (Color)ColorConverter.ConvertFromString(value);
		color.A = (byte)Math.Round(255.0 * Math.Clamp(opacity, 0.0, 1.0));
		SolidColorBrush solidColorBrush = new SolidColorBrush(color);
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private void UpdateSkinAccountState()
	{
		if (SkinsSignInPanel != null && SkinsContentPanel != null)
		{
			bool isSignedIn = _authService.IsSignedIn;
			SkinsSignInPanel.Visibility = !isSignedIn && !_settings.OfflineModeEnabled ? Visibility.Visible : Visibility.Collapsed;
			SkinsContentPanel.Visibility = isSignedIn && !_settings.OfflineModeEnabled ? Visibility.Visible : Visibility.Collapsed;
			ElySkins.Visibility = _settings.OfflineModeEnabled ? Visibility.Visible : Visibility.Collapsed;
            SkinPageSubtitle.Text = LocalizationService.Translate(_settings.OfflineModeEnabled ? "Скин и ник вашего аккаунта Ely.by" : "Сохраните PNG и примените скин к аккаунту Minecraft");
            SkinAccountTitle.Text = _authService.DisplayName + " · Microsoft";
            UpdateSkinLibraryState();
            if (SkinStatusPanel.Visibility == Visibility.Visible) RenderSkinStatus();
			ElySkins.UpdateState();
			LicensedElySkinsCheck.IsChecked = _settings.ShowElySkinsForLicensedAccounts;
		}
	}

	private async Task RefreshSkinPageAsync(bool force = true)
	{
		UpdateSkinAccountState();
		if (_settings.OfflineModeEnabled) { if (force) await ElySkins.RefreshAsync(); return; }
		if (!_authService.IsSignedIn || _skinOperationInProgress || (!force && _skinPageLoaded))
		{
			return;
		}
		SetSkinBusy(busy: true);
		try
		{
			if (force)
			{
				_skinLibraryLoaded = false;
			}
			await EnsureSkinLibraryLoadedAsync();
            _skinPageLoaded = true;
            SkinStatusPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
		{
			_skinPageLoaded = false;
            ShowSkinStatus("Не удалось загрузить библиотеку. Файлы скинов сохранены.", error: true, detail: ex.Message);
            SkinRetryButton.Visibility = Visibility.Visible;
		}
		finally
		{
			SetSkinBusy(busy: false);
		}
	}

	private void SetSkinBusy(bool busy)
	{
        _skinOperationInProgress = busy;
        AddSkinButton.IsEnabled = SkinLibraryItems.IsEnabled = !busy;
        SkinOperationProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) ShowSkinStatus(LocalizationService.Translate("Подготовка скинов…"));
    }

    private void ShowSkinStatus(string message, bool error = false, string? detail = null)
    {
        _skinStatusMessage = message; _skinStatusDetail = detail;
        SkinStatusPanel.Visibility = Visibility.Visible;
        RenderSkinStatus();
        SkinOperationText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "TextBrush");
        SkinRetryButton.Visibility = Visibility.Collapsed;
    }

    private void RenderSkinStatus() => SkinOperationText.Text = LocalizationService.Translate(_skinStatusMessage)
        + (string.IsNullOrEmpty(_skinStatusDetail) ? "" : " " + LocalizationService.TranslateMessage(_skinStatusDetail));

    private async void SkinRetry_Click(object sender, RoutedEventArgs e) => await RefreshSkinPageAsync();

	private async void ChooseSkinButton_Click(object sender, RoutedEventArgs e)
	{
        if (_skinOperationInProgress) return;
        if (_settings.OfflineModeEnabled) { await ElySkins.ChooseAndUploadAsync(); return; }
        if (!_authService.IsSignedIn) { await RefreshSkinPageAsync(); return; }
        try { await EnsureSkinLibraryLoadedAsync(); }
        catch (Exception ex) { ShowSkinStatus("Не удалось загрузить библиотеку. Файлы скинов сохранены.", true, ex.Message); SkinRetryButton.Visibility = Visibility.Visible; return; }
		NewSkinDialog dialog = new NewSkinDialog
		{
			Owner = this
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			SkinLibraryItem item = await _skinLibraryService.ImportAsync(dialog.SkinFilePath, dialog.SkinVariant);
			item.Name = dialog.SkinName;
            await _skinLibraryService.SaveAsync(SkinLibrary.Concat(new[] { item }));
            SkinLibrary.Add(item);
			UpdateSkinLibraryState();
			if (dialog.ApplyAfterSave)
			{
				await ApplyLibrarySkinAsync(item);
			}
			else
			{
				ShowSkinStatus(LocalizationService.Translate("Скин сохранён в библиотеке."));
			}
		}
		catch (Exception ex)
		{
			ShowError("Не удалось открыть скин", ex);
		}
	}

	private async Task EnsureSkinLibraryLoadedAsync()
	{
		if (_skinLibraryLoaded)
		{
			return;
		}
		IReadOnlyList<SkinLibraryItem> obj = await _skinLibraryService.LoadAsync();
		SkinLibrary.Clear();
		foreach (SkinLibraryItem item in obj)
		{
			SkinLibrary.Add(item);
		}
		_skinLibraryLoaded = true;
		UpdateSkinLibraryState();
	}

	private void UpdateSkinLibraryState()
	{
		if (SkinLibraryCountText != null)
		{
			SkinLibraryCountText.Text = (LocalizationService.IsEnglish ? $"{SkinLibrary.Count} saved" : $"Сохранено: {SkinLibrary.Count}");
			SkinLibraryEmptyPanel.Visibility = ((SkinLibrary.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private async void SkinLibraryItem_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is SkinLibraryItem skinLibraryItem && File.Exists(skinLibraryItem.FilePath))
		{
			await ApplyLibrarySkinAsync(skinLibraryItem);
		}
	}

	private async void DeleteSkinLibraryItem_Click(object sender, RoutedEventArgs e)
	{
		if (!((sender as FrameworkElement)?.Tag is SkinLibraryItem skinLibraryItem) || LocalizationService.Show(this, "Удалить скин «" + skinLibraryItem.Name + "» из библиотеки?", "Удаление скина", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
            await _skinLibraryService.DeleteAsync(skinLibraryItem, SkinLibrary.Where(item => item.Id != skinLibraryItem.Id));
            SkinLibrary.Remove(skinLibraryItem);
			UpdateSkinLibraryState();
		}
		catch (Exception ex)
		{
			ShowError("Не удалось удалить скин", ex);
		}
	}

	private async Task ApplyLibrarySkinAsync(SkinLibraryItem item)
	{
		if (_settings.OfflineModeEnabled) { SkinsNav.IsChecked = true; return; }
		if (_skinOperationInProgress || !File.Exists(item.FilePath))
		{
			return;
		}
		SetSkinBusy(busy: true);
		try
		{
            ShowSkinStatus(LocalizationService.Translate("Применяем скин…"));
			byte[] bytes = await File.ReadAllBytesAsync(item.FilePath);
			string accessToken = await _authService.GetRequiredAccessTokenAsync(_settings);
			await _minecraftSkinService.UploadAsync(accessToken, bytes, item.Name + ".png", item.Variant);
			_playerSkinService.RememberSkin(_authService.DisplayName, bytes);
			_skinPageLoaded = true;
            ShowSkinStatus(LocalizationService.Translate("Скин применён к аккаунту Minecraft. Перезапустите игру, чтобы увидеть изменения."));
			await UpdateAccountCardAsync();
		}
		catch (Exception ex)
		{
            ShowSkinStatus("Не удалось применить скин. Он сохранён в библиотеке — повторите попытку.", error: true, detail: ex.Message);
		}
		finally
		{
			SetSkinBusy(busy: false);
		}
	}

	private async Task UpdateAccountCardAsync()
	{
		int revision = ++_accountHeadRevision;
		bool isSignedIn = _authService.IsSignedIn;
		bool offlineModeEnabled = _settings.OfflineModeEnabled;
		OfflinePlayerNamePanel.Visibility = offlineModeEnabled && _elyAccounts.IsSignedIn ? Visibility.Collapsed : Visibility.Visible;
		if (offlineModeEnabled) { await UpdateElyAccountCardAsync(revision); return; }
		string text = LocalizationService.Translate(_authService.DisplayName);
		AccountNameText.Text = text;
		SettingsAccountNameText.Text = text;
		AccountModeText.Text = LocalizationService.Translate(isSignedIn ? "Microsoft • Minecraft" : "Аккаунт не подключён");
		SettingsAccountHintText.Text = LocalizationService.Translate(isSignedIn ? "Лицензионная сессия Microsoft активна" : "Для запуска требуется Microsoft-аккаунт");
		AccountStatusDot.Fill = isSignedIn ? new SolidColorBrush(Color.FromRgb(61, 220, 164)) : new SolidColorBrush(Color.FromRgb(94, 102, 123));
		SettingsLoginButton.Visibility = (isSignedIn ? Visibility.Collapsed : Visibility.Visible);
		SignOutButton.Visibility = ((!isSignedIn) ? Visibility.Collapsed : Visibility.Visible);
		GateLoginButton.Visibility = (isSignedIn ? Visibility.Collapsed : Visibility.Visible);
		UpdateSkinAccountState();
		string? owner = isSignedIn && !offlineModeEnabled ? _authService.DisplayName : null;
		if (!string.Equals(_accountHeadOwner, owner, StringComparison.OrdinalIgnoreCase))
		{
			_accountHeadOwner = owner;
			ShowAccountHead(null);
		}
		if (owner is not null && _playerSkinService.GetCachedHead(owner) is { } cachedHead)
			ShowAccountHead(cachedHead);
		if (!isSignedIn || offlineModeEnabled)
		{
			return;
		}
		try
		{
			var head = await _playerSkinService.GetHeadAsync(owner!);
			if (revision == _accountHeadRevision && head is not null &&
				_authService.IsSignedIn && !_settings.OfflineModeEnabled &&
				string.Equals(owner, _authService.DisplayName, StringComparison.OrdinalIgnoreCase))
				ShowAccountHead(head);
		}
		catch (Exception)
		{
		}
	}

	private void ShowAccountHead(PlayerHeadImage? head)
	{
		AccountFaceImage.Source = SettingsFaceImage.Source = head?.Image;
		AccountFaceImage.Visibility = SettingsFaceImage.Visibility = head is null ? Visibility.Collapsed : Visibility.Visible;
		AccountFallbackGlyph.Visibility = SettingsAccountFallbackGlyph.Visibility = head is null ? Visibility.Visible : Visibility.Collapsed;
	}

	private async Task SetInstanceIconAsync(GameInstance instance, string sourcePath)
	{
		string text = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
		string text2 = System.IO.Path.Combine(".orbit", "icon" + text);
		Directory.CreateDirectory(System.IO.Path.Combine(instance.DirectoryPath, ".orbit"));
		File.Copy(sourcePath, System.IO.Path.Combine(instance.DirectoryPath, text2), overwrite: true);
		instance.IconFile = text2;
		await _instanceRepository.SaveAsync(instance);
	}

	private void CreateMicrosoftAccount_Click(object sender, RoutedEventArgs e)
	{
		OpenWeb("https://signup.live.com/");
	}

	private async void OfflineModeCheck_Changed(object sender, RoutedEventArgs e)
	{
		if (OfflinePlayerNamePanel == null)
		{
			return;
		}
		OfflinePlayerNamePanel.IsEnabled = OfflineModeCheck.IsChecked == true;
		if (base.IsLoaded && !_populatingSettings)
		{
			_settings.OfflineModeEnabled = OfflineModeCheck.IsChecked == true;
			string text = OfflinePlayerNameBox.Text.Trim();
			if (Regex.IsMatch(text, "^[A-Za-z0-9_]{3,16}$"))
			{
				_settings.OfflinePlayerName = text;
			}
			else if (_settings.OfflineModeEnabled)
			{
				TextBox offlinePlayerNameBox = OfflinePlayerNameBox;
				string text2 = (_settings.OfflinePlayerName = "OrbitPlayer");
				offlinePlayerNameBox.Text = text2;
			}
			await _settingsService.SaveAsync(_settings);
			await UpdateAccountCardAsync();
		}
	}

	private async void OfflinePlayerNameBox_LostFocus(object sender, RoutedEventArgs e)
	{
		if (base.IsLoaded && !_populatingSettings)
		{
			string text = OfflinePlayerNameBox.Text.Trim();
			if (!Regex.IsMatch(text, "^[A-Za-z0-9_]{3,16}$"))
			{
				OfflinePlayerNameBox.Text = _settings.OfflinePlayerName;
				LocalizationService.Show(this, "Ник игрока должен содержать от 3 до 16 латинских букв, цифр или знаков подчёркивания.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			else
			{
				_settings.OfflinePlayerName = text;
				await _settingsService.SaveAsync(_settings);
				await UpdateAccountCardAsync();
			}
		}
	}

	private static void OpenWeb(string url)
	{
		Process.Start(new ProcessStartInfo(url)
		{
			UseShellExecute = true
		});
	}

	private void ShowStatus(string message)
	{
		LaunchStatusBar.Visibility = Visibility.Visible;
		LaunchInstanceText.Text = message;
		LaunchStatusText.Text = LocalizationService.Translate("Готово");
		LaunchProgressBar.Value = 100.0;
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(3.0)
		};
		timer.Tick += delegate
		{
			if (_launchCancellation == null)
			{
				LaunchStatusBar.Visibility = Visibility.Collapsed;
			}
			timer.Stop();
		};
		timer.Start();
	}

	private void ShowError(string title, Exception ex)
	{
		LocalizationService.Show(this, title + ".\n\n" + ex.Message, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Hand);
	}

	private static void OpenFolder(string path)
	{
		Directory.CreateDirectory(path);
		Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
		{
			UseShellExecute = true
		});
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			value = value.Replace(oldChar, '_');
		}
		return value;
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
		}
		else
		{
			DragMove();
		}
	}

	private void MinimizeButton_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void MaximizeButton_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}












}
