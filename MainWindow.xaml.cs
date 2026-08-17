using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new();
    private readonly InstanceRepository _instanceRepository = new();
    private readonly SettingsService _settingsService = new();
    private readonly AuthService _authService;
    private readonly PlayerSkinService _playerSkinService;
    private readonly MinecraftService _minecraftService;
    private LauncherSettings _settings = new();
    private CancellationTokenSource? _launchCancellation;
    private readonly List<InstalledMod> _allInstalledContent = [];
    private bool _populatingSettings;

    public ObservableCollection<GameInstance> Instances { get; } = [];
    public ObservableCollection<GameInstance> VisibleInstances { get; } = [];
    public ObservableCollection<InstalledMod> InstalledMods { get; } = [];
    public ObservableCollection<DownloadItem> Downloads { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitLauncher/1.2 (Minecraft desktop launcher)");
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _authService = new AuthService(_httpClient);
        _playerSkinService = new PlayerSkinService(_httpClient);
        _minecraftService = new MinecraftService(_httpClient);
        DataContext = this;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        LocalizationService.SetLanguage(_settings.Language);
        LocalizationService.Apply(this);
        PopulateSettingsControls();
        await RefreshInstancesAsync();
        await _authService.TryRestoreSessionAsync(_settings);
        await UpdateAccountCardAsync();
        LocalizationService.Apply(this);
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
            OfflinePlayerNameBox.Text = string.IsNullOrWhiteSpace(_settings.OfflinePlayerName) ? "OrbitPlayer" : _settings.OfflinePlayerName;
            OfflineModeCheck.IsChecked = _settings.OfflineModeEnabled;
            OfflinePlayerNamePanel.IsEnabled = _settings.OfflineModeEnabled;
            DependencyDownloadModeCombo.SelectedValue = NormalizeDependencyMode(_settings.ModDependencyDownloadMode);
            AutoInstallFabricApiCheck.IsChecked = _settings.AutoInstallFabricApi;
            LanguageCombo.SelectedValue = NormalizeLanguage(_settings.Language);
        }
        finally
        {
            _populatingSettings = false;
        }
    }

    private async Task RefreshInstancesAsync(string? selectId = null)
    {
        selectId ??= (ModsInstanceCombo.SelectedItem as GameInstance)?.Id;
        var loaded = await _instanceRepository.GetAllAsync();
        Instances.Clear();
        foreach (var instance in loaded)
            Instances.Add(instance);

        ApplyInstanceFilter();
        ModsInstanceCombo.SelectedItem = Instances.FirstOrDefault(x => x.Id == selectId) ?? Instances.FirstOrDefault();
        RefreshInstalledMods();
    }

    private void ApplyInstanceFilter()
    {
        var query = InstanceSearchBox?.Text?.Trim() ?? string.Empty;
        VisibleInstances.Clear();
        foreach (var instance in Instances.Where(x => string.IsNullOrEmpty(query)
                                                       || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                                       || x.GameVersion.Contains(query, StringComparison.OrdinalIgnoreCase)
                                                       || x.Loader.Contains(query, StringComparison.OrdinalIgnoreCase)))
            VisibleInstances.Add(instance);

        if (InstanceCountText is not null)
            InstanceCountText.Text = LocalizationService.IsEnglish
                ? $"{VisibleInstances.Count} of {Instances.Count}"
                : $"{VisibleInstances.Count} из {Instances.Count}";
        if (EmptyInstancesPanel is not null)
            EmptyInstancesPanel.Visibility = VisibleInstances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstanceSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyInstanceFilter();

    private async void CreateInstanceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateInstanceDialog(_httpClient, _settings) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        var created = await _instanceRepository.CreateAsync(dialog.Result);
        if (!string.IsNullOrWhiteSpace(dialog.SelectedIconPath))
            await SetInstanceIconAsync(created, dialog.SelectedIconPath);

        string? fabricApiError = null;
        if (_settings.AutoInstallFabricApi && string.Equals(created.Loader, "Fabric", StringComparison.OrdinalIgnoreCase))
            fabricApiError = await InstallFabricApiAsync(created);

        await RefreshInstancesAsync(created.Id);
        ShowStatus(fabricApiError is null
            ? $"Сборка «{created.Name}» создана."
            : $"Сборка «{created.Name}» создана без Fabric API.");
        if (fabricApiError is not null)
        {
            LocalizationService.Show(this,
                $"Сборка создана, но Fabric API не удалось установить автоматически. Его можно установить позже через каталог модов.\n\n{fabricApiError}",
                "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<string?> InstallFabricApiAsync(GameInstance instance)
    {
        LaunchStatusBar.Visibility = Visibility.Visible;
        LaunchInstanceText.Text = LocalizationService.IsEnglish ? $"Configuring “{instance.Name}”" : $"Настраиваем «{instance.Name}»";
        LaunchStatusText.Text = LocalizationService.Translate("Установка Fabric API…");
        LaunchProgressBar.Value = 0;

        var progress = new Progress<double>(value =>
        {
            LaunchProgressBar.Value = Math.Clamp(value, 0, 100);
            LaunchStatusText.Text = $"Fabric API • {value:0}%";
        });

        try
        {
            var service = new ModrinthService(_httpClient, _settings.ModrinthApiBase);
            var project = new ModSearchResult
            {
                ProjectId = AppConfiguration.FabricApiProjectId,
                SourceId = "modrinth",
                Title = "Fabric API",
                ProjectType = "mod"
            };
            await service.DownloadAsync(project, instance, instance.GameVersion, "fabric", "required", progress);
            LaunchProgressBar.Value = 100;
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
        var picker = new OpenFileDialog { Filter = "Orbit / ZIP package (*.zip)|*.zip|Все файлы (*.*)|*.*" };
        if (picker.ShowDialog(this) != true)
            return;

        try
        {
            var imported = await _instanceRepository.ImportAsync(picker.FileName);
            await RefreshInstancesAsync(imported.Id);
            ShowStatus($"Сборка «{imported.Name}» импортирована.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось импортировать сборку", ex);
        }
    }

    private async void LaunchCardButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameInstance instance)
            await LaunchInstanceAsync(instance);
    }

    private async void InstanceCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is GameInstance instance)
            await LaunchInstanceAsync(instance);
    }

    private async void ContextLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is { } instance)
            await LaunchInstanceAsync(instance);
    }

    private async Task LaunchInstanceAsync(GameInstance instance)
    {
        if (!_settings.OfflineModeEnabled && !_authService.IsSignedIn)
        {
            LocalizationService.Show(this, "Для запуска игры войдите в Microsoft-аккаунт, которому принадлежит Minecraft Java Edition. Библиотекой и настройками можно пользоваться без входа.", "Требуется аккаунт", MessageBoxButton.OK, MessageBoxImage.Information);
            SettingsNav.IsChecked = true;
            return;
        }

        if (_launchCancellation is not null)
        {
            LocalizationService.Show(this, "Другая сборка уже готовится к запуску.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var download = new DownloadItem { Title = instance.Name, Subtitle = instance.Subtitle, Status = LocalizationService.Translate("Подготовка") };
        Downloads.Insert(0, download);
        _launchCancellation = new CancellationTokenSource();
        LaunchStatusBar.Visibility = Visibility.Visible;
        LaunchInstanceText.Text = LocalizationService.IsEnglish ? $"Launching: {instance.Name}" : $"Запуск: {instance.Name}";
        LaunchStatusText.Text = LocalizationService.Translate("Подготовка…");
        LaunchProgressBar.Value = 0;

        var progress = new Progress<LaunchProgress>(value =>
        {
            LaunchProgressBar.Value = Math.Clamp(value.Percent, 0, 100);
            LaunchStatusText.Text = LocalizationService.Translate(value.Status);
            download.Progress = Math.Clamp(value.Percent, 0, 100);
            download.Status = LocalizationService.Translate(value.Status);
        });

        try
        {
            var session = _settings.OfflineModeEnabled
                ? CmlLib.Core.Auth.MSession.CreateOfflineSession(_settings.OfflinePlayerName)
                : await _authService.GetRequiredSessionAsync(_settings);
            await UpdateAccountCardAsync();
            var result = await _minecraftService.InstallAndLaunchAsync(
                instance,
                session,
                _settings,
                progress,
                message => Debug.WriteLine(message),
                _launchCancellation.Token);

            instance.LaunchVersionId = result.VersionId;
            instance.LastPlayedAt = DateTime.UtcNow;
            await _instanceRepository.SaveAsync(instance);
            download.Progress = 100;
            download.Status = LocalizationService.IsEnglish ? $"Launched, PID {result.ProcessId}" : $"Запущено, PID {result.ProcessId}";
            LaunchStatusBar.Visibility = Visibility.Collapsed;
            if (_settings.CloseLauncherOnGameStart)
                WindowState = WindowState.Minimized;
            await RefreshInstancesAsync(instance.Id);
        }
        catch (OperationCanceledException)
        {
            download.Status = LocalizationService.Translate("Отменено");
            LaunchStatusText.Text = LocalizationService.Translate("Запуск отменён");
        }
        catch (Exception ex)
        {
            download.Status = LocalizationService.Translate("Ошибка");
            ShowError("Не удалось запустить Minecraft", ex);
        }
        finally
        {
            _launchCancellation.Dispose();
            _launchCancellation = null;
            LaunchStatusBar.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelLaunchButton_Click(object sender, RoutedEventArgs e) => _launchCancellation?.Cancel();

    private async void ContextEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance)
            return;
        var dialog = new EditInstanceDialog(_httpClient, _settings, instance) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _instanceRepository.SaveAsync(instance);
            await RefreshInstancesAsync(instance.Id);
        }
    }

    private async void ContextRename_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance)
            return;
        var dialog = new TextPromptDialog("Переименовать сборку", "Новое название", instance.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResultText))
        {
            instance.Name = dialog.ResultText.Trim();
            await _instanceRepository.SaveAsync(instance);
            await RefreshInstancesAsync(instance.Id);
        }
    }

    private async void ContextIcon_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance)
            return;
        var picker = new OpenFileDialog { Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (picker.ShowDialog(this) != true)
            return;

        await SetInstanceIconAsync(instance, picker.FileName);
        await RefreshInstancesAsync(instance.Id);
    }

    private void ContextMods_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is { } instance)
            OpenModBrowser(instance);
    }

    private async void ContextDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance)
            return;
        try
        {
            var clone = await _instanceRepository.DuplicateAsync(instance);
            await RefreshInstancesAsync(clone.Id);
            ShowStatus("Копия сборки создана.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось дублировать сборку", ex);
        }
    }

    private async void ContextExport_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance)
            return;
        var picker = new SaveFileDialog
        {
            Filter = "ZIP-архив (*.zip)|*.zip",
            FileName = SanitizeFileName(instance.Name) + ".zip"
        };
        if (picker.ShowDialog(this) != true)
            return;

        try
        {
            await _instanceRepository.ExportAsync(instance, picker.FileName);
            ShowStatus("Сборка экспортирована.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось экспортировать сборку", ex);
        }
    }

    private void ContextFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is { } instance)
            OpenFolder(instance.DirectoryPath);
    }

    private async void ContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is not { } instance)
            return;
        var answer = LocalizationService.Show(
            this,
            $"Удалить сборку «{instance.Name}» вместе с её мирами, модами и настройками?\n\nЭто действие нельзя отменить.",
            "Удаление сборки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            VisibleInstances.Remove(instance);
            Instances.Remove(instance);
            InstalledMods.Clear();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await _instanceRepository.DeleteAsync(instance);
            ShowStatus($"Сборка «{instance.Name}» удалена.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось удалить сборку", ex);
        }
        finally
        {
            await RefreshInstancesAsync();
        }
    }

    private static GameInstance? ContextInstance(object sender) => (sender as FrameworkElement)?.DataContext as GameInstance;

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton button)
            return;

        LibraryPage.Visibility = button.Tag?.ToString() == "Library" ? Visibility.Visible : Visibility.Collapsed;
        ModsPage.Visibility = button.Tag?.ToString() == "Mods" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = button.Tag?.ToString() == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (button.Tag?.ToString() == "Mods")
            RefreshInstalledMods();
    }

    private void ModsInstanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshInstalledMods();

    private void ContentFolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshInstalledMods();

    private void ContentSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyInstalledContentView();

    private void ContentFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyInstalledContentView();

    private string SelectedContentKind => (ContentFolderCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mods";

    private string GetSelectedContentDirectory(GameInstance instance) => SelectedContentKind switch
    {
        "resourcepacks" => Path.Combine(instance.DirectoryPath, "resourcepacks"),
        "shaderpacks" => Path.Combine(instance.DirectoryPath, "shaderpacks"),
        "downloads" => Path.Combine(instance.DirectoryPath, ".orbit", "downloads"),
        _ => instance.ModsPath
    };

    private string SelectedContentType => SelectedContentKind switch
    {
        "resourcepacks" => "Ресурспак",
        "shaderpacks" => "Шейдер",
        "downloads" => "Скачанный набор",
        _ => "Мод"
    };

    private string SelectedContentLabel => LocalizationService.Translate(SelectedContentKind switch
    {
        "resourcepacks" => "Ресурспаки",
        "shaderpacks" => "Шейдеры",
        "downloads" => "Скачанные наборы",
        _ => "Моды"
    });

    private void RefreshInstalledMods()
    {
        _allInstalledContent.Clear();
        InstalledMods.Clear();
        if (ModsInstanceCombo?.SelectedItem is not GameInstance instance)
        {
            if (ModsSummaryText is not null)
                ModsSummaryText.Text = LocalizationService.Translate("Выберите сборку");
            UpdateContentStatistics(null);
            UpdateContentEmptyState();
            return;
        }

        UpdateContentStatistics(instance);
        if (EnableAllContentButton is not null && DisableAllContentButton is not null)
        {
            var visibility = SelectedContentKind == "downloads" ? Visibility.Collapsed : Visibility.Visible;
            EnableAllContentButton.Visibility = visibility;
            DisableAllContentButton.Visibility = visibility;
        }
        var directory = GetSelectedContentDirectory(instance);
        Directory.CreateDirectory(directory);
        var searchOption = SelectedContentKind == "downloads" ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(directory, "*", searchOption)
                     .Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName))
        {
            var info = new FileInfo(file);
            _allInstalledContent.Add(new InstalledMod
            {
                Name = info.Name.Replace(".disabled", string.Empty, StringComparison.OrdinalIgnoreCase),
                FullPath = file,
                IsEnabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                SizeBytes = info.Length,
                TypeText = SelectedContentType,
                ModifiedAt = info.LastWriteTime,
                FolderText = Path.GetRelativePath(directory, info.DirectoryName ?? directory),
                CanToggle = SelectedContentKind != "downloads"
            });
        }

        ApplyInstalledContentView();
    }

    private void ApplyInstalledContentView()
    {
        if (InstalledMods is null)
            return;

        var query = ContentSearchBox?.Text?.Trim() ?? string.Empty;
        var state = (ContentStateCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var sort = (ContentSortCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "name";
        IEnumerable<InstalledMod> filtered = _allInstalledContent;

        if (query.Length > 0)
            filtered = filtered.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        filtered = state switch
        {
            "enabled" => filtered.Where(item => item.IsEnabled),
            "disabled" => filtered.Where(item => !item.IsEnabled),
            _ => filtered
        };
        filtered = sort switch
        {
            "size" => filtered.OrderByDescending(item => item.SizeBytes).ThenBy(item => item.Name),
            "date" => filtered.OrderByDescending(item => item.ModifiedAt).ThenBy(item => item.Name),
            _ => filtered.OrderBy(item => item.Name)
        };

        InstalledMods.Clear();
        foreach (var item in filtered)
            InstalledMods.Add(item);

        if (ModsSummaryText is not null && ModsInstanceCombo?.SelectedItem is GameInstance instance)
            ModsSummaryText.Text = LocalizationService.IsEnglish
                ? $"{instance.Name}  •  {SelectedContentLabel}  •  showing {InstalledMods.Count} of {_allInstalledContent.Count}"
                : $"{instance.Name}  •  {SelectedContentLabel}  •  показано {InstalledMods.Count} из {_allInstalledContent.Count}";
        UpdateContentEmptyState();
    }

    private void UpdateContentEmptyState()
    {
        if (ModsEmptyPanel is null || ModsEmptyTitle is null)
            return;
        ModsEmptyPanel.Visibility = InstalledMods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModsEmptyTitle.Text = LocalizationService.Translate(_allInstalledContent.Count == 0 ? "Здесь пока пусто" : "По фильтрам ничего не найдено");
    }

    private void UpdateContentStatistics(GameInstance? instance)
    {
        if (ModsCountText is null || ResourcePacksCountText is null || ShadersCountText is null || DownloadedCountText is null)
            return;
        ModsCountText.Text = instance is null ? "0" : CountContentFiles(instance.ModsPath, false).ToString();
        ResourcePacksCountText.Text = instance is null ? "0" : CountContentFiles(Path.Combine(instance.DirectoryPath, "resourcepacks"), false).ToString();
        ShadersCountText.Text = instance is null ? "0" : CountContentFiles(Path.Combine(instance.DirectoryPath, "shaderpacks"), false).ToString();
        DownloadedCountText.Text = instance is null ? "0" : CountContentFiles(Path.Combine(instance.DirectoryPath, ".orbit", "downloads"), true).ToString();
    }

    private static int CountContentFiles(string directory, bool recursive)
    {
        if (!Directory.Exists(directory))
            return 0;
        try
        {
            return Directory.EnumerateFiles(directory, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Count(IsSupportedContentFile);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsSupportedContentFile(string path) =>
        path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

    private void BrowseModsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModsInstanceCombo.SelectedItem is GameInstance instance)
            OpenModBrowser(instance);
        else
            LocalizationService.Show(this, "Сначала выберите сборку.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenModBrowser(GameInstance instance)
    {
        var browser = new ModBrowserWindow(
            _httpClient,
            instance,
            Downloads,
            _settings.ModrinthApiBase,
            _settings.ModDependencyDownloadMode,
            _settings.CurseForgeApiBase,
            _settings.CurseForgeApiKey) { Owner = this };
        browser.ShowDialog();
        RefreshInstalledMods();
        _ = RefreshInstancesAsync(instance.Id);
    }

    private void OpenModsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModsInstanceCombo.SelectedItem is GameInstance instance)
        {
            var directory = GetSelectedContentDirectory(instance);
            Directory.CreateDirectory(directory);
            OpenFolder(directory);
        }
    }

    private void RefreshModsButton_Click(object sender, RoutedEventArgs e) => RefreshInstalledMods();

    private void EnableAllContent_Click(object sender, RoutedEventArgs e) => SetAllContentEnabled(true);

    private void DisableAllContent_Click(object sender, RoutedEventArgs e) => SetAllContentEnabled(false);

    private void SetAllContentEnabled(bool enabled)
    {
        var failed = 0;
        foreach (var item in _allInstalledContent.Where(item => item.CanToggle && item.IsEnabled != enabled).ToList())
        {
            try
            {
                var destination = enabled ? item.FullPath[..^".disabled".Length] : item.FullPath + ".disabled";
                if (File.Exists(destination))
                {
                    failed++;
                    continue;
                }
                File.Move(item.FullPath, destination);
            }
            catch
            {
                failed++;
            }
        }
        RefreshInstalledMods();
        if (failed > 0)
            LocalizationService.Show(this, $"Не удалось изменить состояние файлов: {failed}.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ToggleModButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not InstalledMod mod)
            return;
        try
        {
            var destination = mod.IsEnabled ? mod.FullPath + ".disabled" : mod.FullPath[..^".disabled".Length];
            File.Move(mod.FullPath, destination, false);
            RefreshInstalledMods();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось изменить состояние файла", ex);
        }
    }

    private void DeleteModButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not InstalledMod mod)
            return;
        if (LocalizationService.Show(this, $"Удалить файл «{mod.Name}»?", "Удаление контента", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            File.Delete(mod.FullPath);
            RefreshInstalledMods();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось удалить файл", ex);
        }
    }

    private void ShowContentFileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not InstalledMod item || !File.Exists(item.FullPath))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
    }

    private void ModsPage_DragOver(object sender, DragEventArgs e)
    {
        if (ModsInstanceCombo?.SelectedItem is not GameInstance || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var files = (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? [];
        e.Effects = files.Any(IsAllowedDropFile) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ModsPage_Drop(object sender, DragEventArgs e)
    {
        if (ModsInstanceCombo?.SelectedItem is not GameInstance instance || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;
        var destinationDirectory = GetSelectedContentDirectory(instance);
        Directory.CreateDirectory(destinationDirectory);
        var installed = 0;
        var skipped = 0;
        foreach (var source in files.Where(File.Exists))
        {
            if (!IsAllowedDropFile(source))
            {
                skipped++;
                continue;
            }
            try
            {
                var destination = UniqueDestinationPath(destinationDirectory, Path.GetFileName(source));
                File.Copy(source, destination, false);
                installed++;
            }
            catch
            {
                skipped++;
            }
        }
        RefreshInstalledMods();
        if (installed > 0 || skipped > 0)
            LocalizationService.Show(this, $"Добавлено файлов: {installed}. Пропущено: {skipped}.", "Orbit Launcher", MessageBoxButton.OK, skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private bool IsAllowedDropFile(string path) => SelectedContentKind switch
    {
        "mods" => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase),
        "resourcepacks" or "shaderpacks" => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase),
        "downloads" => path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static string UniqueDestinationPath(string directory, string fileName)
    {
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination))
            return destination;
        var extension = Path.GetExtension(fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        for (var index = 2; ; index++)
        {
            destination = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(destination))
                return destination;
        }
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettingsControls())
            return;
        LocalizationService.SetLanguage(_settings.Language);
        await _settingsService.SaveAsync(_settings);
        LocalizationService.Apply(this);
        await RefreshInstancesAsync();
        await UpdateAccountCardAsync();
        ShowStatus(LocalizationService.Translate("Настройки сохранены."));
    }

    private async void MicrosoftLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var clientId = _settings.MicrosoftClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            LocalizationService.Show(this, "Microsoft Client ID не задан в конфигурации этой сборки. Обратитесь к автору лаунчера.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GateLoginButton.IsEnabled = false;
        SettingsLoginButton.IsEnabled = false;
        GateLoginButton.Content = LocalizationService.Translate("Открываем Microsoft…");
        SettingsLoginButton.Content = LocalizationService.Translate("Открываем Microsoft…");
        try
        {
            await _authService.LoginInteractivelyAsync(_settings);
            await UpdateAccountCardAsync();
            ShowStatus("Вход в Microsoft выполнен безопасно.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось войти в Microsoft", ex);
        }
        finally
        {
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
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        await UpdateAccountCardAsync();
    }

    private bool TryReadSettingsControls()
    {
        if (!int.TryParse(DefaultMinRamBox.Text, out var minRam) || !int.TryParse(DefaultMaxRamBox.Text, out var maxRam)
            || minRam < 512 || maxRam < minRam || maxRam > 65536)
        {
            LocalizationService.Show(this, "Проверьте объём памяти: минимум от 512 МБ, максимум не меньше минимума.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            var offlineName = OfflinePlayerNameBox.Text.Trim();
            if (OfflineModeCheck.IsChecked == true && !System.Text.RegularExpressions.Regex.IsMatch(offlineName, "^[A-Za-z0-9_]{3,16}$"))
                throw new InvalidOperationException("Имя офлайн-игрока должно содержать от 3 до 16 латинских букв, цифр или знаков подчёркивания.");
            _settings.DefaultMinimumRamMb = minRam;
            _settings.DefaultMaximumRamMb = maxRam;
            _settings.ShowSnapshots = ShowSnapshotsCheck.IsChecked == true;
            _settings.CloseLauncherOnGameStart = CloseOnLaunchCheck.IsChecked == true;
            _settings.OfflineModeEnabled = OfflineModeCheck.IsChecked == true;
            _settings.OfflinePlayerName = string.IsNullOrWhiteSpace(offlineName) ? "OrbitPlayer" : offlineName;
            _settings.ModDependencyDownloadMode = NormalizeDependencyMode((DependencyDownloadModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
            _settings.AutoInstallFabricApi = AutoInstallFabricApiCheck.IsChecked == true;
            _settings.Language = NormalizeLanguage((LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        }
        catch (InvalidOperationException ex)
        {
            LocalizationService.Show(this, ex.Message, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private static string NormalizeDependencyMode(string? value) => value is "all" or "none" ? value : "required";
    private static string NormalizeLanguage(string? value) => string.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";

    private async Task UpdateAccountCardAsync()
    {
        var signedIn = _authService.IsSignedIn;
        var offline = _settings.OfflineModeEnabled;
        var displayName = offline ? _settings.OfflinePlayerName : LocalizationService.Translate(_authService.DisplayName);
        AccountNameText.Text = displayName;
        SettingsAccountNameText.Text = displayName;
        AccountModeText.Text = LocalizationService.Translate(offline ? "Офлайн • тестовый режим" : signedIn ? "Microsoft • Minecraft" : "Аккаунт не подключён");
        SettingsAccountHintText.Text = LocalizationService.Translate(offline ? "Офлайн-режим включён в настройках" : signedIn ? "Лицензионная сессия Microsoft активна" : "Для запуска требуется Microsoft-аккаунт");
        AccountStatusDot.Fill = offline
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 166, 35))
            : signedIn
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 220, 164))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 102, 123));
        SettingsLoginButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        GateLoginButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;

        AccountFaceImage.Source = null;
        AccountOverlayImage.Source = null;
        SettingsFaceImage.Source = null;
        SettingsOverlayImage.Source = null;
        AccountFallbackGlyph.Visibility = Visibility.Visible;
        if (!signedIn || offline)
            return;

        try
        {
            var head = await _playerSkinService.GetHeadAsync(_authService.DisplayName);
            if (head is null)
                return;

            AccountFaceImage.Source = head.Face;
            AccountOverlayImage.Source = head.Overlay;
            SettingsFaceImage.Source = head.Face;
            SettingsOverlayImage.Source = head.Overlay;
            AccountFallbackGlyph.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Не удалось загрузить голову скина: {ex.Message}");
        }
    }

    private async Task SetInstanceIconAsync(GameInstance instance, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var relative = Path.Combine(".orbit", "icon" + extension);
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, ".orbit"));
        File.Copy(sourcePath, Path.Combine(instance.DirectoryPath, relative), true);
        instance.IconFile = relative;
        await _instanceRepository.SaveAsync(instance);
    }

    private void CreateMicrosoftAccount_Click(object sender, RoutedEventArgs e) =>
        OpenWeb("https://signup.live.com/");

    private async void OfflineModeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (OfflinePlayerNamePanel is null)
            return;
        OfflinePlayerNamePanel.IsEnabled = OfflineModeCheck.IsChecked == true;
        if (!IsLoaded || _populatingSettings)
            return;
        _settings.OfflineModeEnabled = OfflineModeCheck.IsChecked == true;
        var name = OfflinePlayerNameBox.Text.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
            _settings.OfflinePlayerName = name;
        else if (_settings.OfflineModeEnabled)
            OfflinePlayerNameBox.Text = _settings.OfflinePlayerName = "OrbitPlayer";
        await _settingsService.SaveAsync(_settings);
        await UpdateAccountCardAsync();
    }

    private async void OfflinePlayerNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _populatingSettings)
            return;
        var name = OfflinePlayerNameBox.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
        {
            OfflinePlayerNameBox.Text = _settings.OfflinePlayerName;
            LocalizationService.Show(this, "Имя офлайн-игрока должно содержать от 3 до 16 латинских букв, цифр или знаков подчёркивания.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.OfflinePlayerName = name;
        await _settingsService.SaveAsync(_settings);
        await UpdateAccountCardAsync();
    }

    private static void OpenWeb(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void ShowStatus(string message)
    {
        LaunchStatusBar.Visibility = Visibility.Visible;
        LaunchInstanceText.Text = message;
        LaunchStatusText.Text = LocalizationService.Translate("Готово");
        LaunchProgressBar.Value = 100;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            if (_launchCancellation is null)
                LaunchStatusBar.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    private void ShowError(string title, Exception ex) => LocalizationService.Show(this, $"{title}.\n\n{ex.Message}", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Error);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
