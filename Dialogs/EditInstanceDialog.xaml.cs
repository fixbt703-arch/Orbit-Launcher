using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];

    public EditInstanceDialog(HttpClient httpClient, LauncherSettings settings, GameInstance instance)
    {
        InitializeComponent();
        _instance = instance;
        _loaderVersionService = new LoaderVersionService(httpClient, settings);
        _originalGameVersion = instance.GameVersion;
        _originalLoader = instance.Loader;
        _originalLoaderVersion = instance.LoaderVersion;
        _originalLaunchVersionId = instance.LaunchVersionId;
        DataContext = this;

        NameBox.Text = instance.Name;
        GameVersionBox.Text = instance.GameVersion;
        LoaderBox.SelectedIndex = instance.Loader switch
        {
            "Fabric" => 1,
            "Forge" => 2,
            "NeoForge" => 3,
            "Quilt" => 4,
            _ => 0
        };
        MinRamBox.Text = instance.MinimumRamMb.ToString();
        MaxRamBox.Text = instance.MaximumRamMb.ToString();
        JavaPathBox.Text = instance.JavaPath;
        LaunchVersionBox.Text = instance.LaunchVersionId;
        LocalizationService.Apply(this);
        Loaded += async (_, _) =>
        {
            await RefreshLoaderVersionsAsync(_originalLoaderVersion);
            LocalizationService.Apply(this);
        };
        Closed += (_, _) => _refreshCancellation?.Cancel();
    }

    private string SelectedLoader => (LoaderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";

    private async void LoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await RefreshLoaderVersionsAsync();
    }

    private async void GameVersionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        await Task.Delay(350);
        if (GameVersionBox.IsKeyboardFocusWithin)
            await RefreshLoaderVersionsAsync();
    }

    private async Task RefreshLoaderVersionsAsync(string? selectVersion = null)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        LoaderVersions.Clear();
        LoaderVersionBox.IsEnabled = false;

        var loader = SelectedLoader;
        if (loader == "Vanilla")
        {
            LoaderVersionStatusText.Text = LocalizationService.Translate("Не требуется");
            return;
        }

        LoaderVersionStatusText.Text = LocalizationService.Translate("Загрузка…");
        try
        {
            var versions = await _loaderVersionService.GetVersionsAsync(loader, GameVersionBox.Text.Trim(), _refreshCancellation.Token);
            foreach (var version in versions)
                LoaderVersions.Add(version);
            LoaderVersionBox.IsEnabled = LoaderVersions.Count > 0;
            LoaderVersionBox.SelectedValue = selectVersion ?? _instance.LoaderVersion;
            if (LoaderVersionBox.SelectedIndex < 0 && LoaderVersions.Count > 0)
                LoaderVersionBox.SelectedIndex = 0;
            LoaderVersionStatusText.Text = LoaderVersions.Count > 0 ? $"Доступно: {LoaderVersions.Count}" : "Нет совместимых версий";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            LoaderVersionStatusText.Text = LocalizationService.Translate("Список недоступен");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var gameVersion = GameVersionBox.Text.Trim();
        var loader = SelectedLoader;
        var loaderVersion = (LoaderVersionBox.SelectedItem as LoaderVersionInfo)?.Id ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(gameVersion))
        {
            LocalizationService.Show(this, "Название и версия Minecraft не могут быть пустыми.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (loader != "Vanilla" && string.IsNullOrWhiteSpace(loaderVersion))
        {
            LocalizationService.Show(this, "Выберите совместимую версию загрузчика.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MinRamBox.Text, out var minRam) || !int.TryParse(MaxRamBox.Text, out var maxRam)
            || minRam < 512 || maxRam < minRam || maxRam > 65536)
        {
            LocalizationService.Show(this, "Проверьте значения оперативной памяти.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var definitionChanged = !string.Equals(_originalGameVersion, gameVersion, StringComparison.OrdinalIgnoreCase)
                                || !string.Equals(_originalLoader, loader, StringComparison.OrdinalIgnoreCase)
                                || !string.Equals(_originalLoaderVersion, loaderVersion, StringComparison.OrdinalIgnoreCase);

        _instance.Name = name;
        _instance.GameVersion = gameVersion;
        _instance.Loader = loader;
        _instance.LoaderVersion = loaderVersion;
        _instance.MinimumRamMb = minRam;
        _instance.MaximumRamMb = maxRam;
        _instance.JavaPath = JavaPathBox.Text.Trim();
        var typedLaunchId = LaunchVersionBox.Text.Trim();
        _instance.LaunchVersionId = definitionChanged && typedLaunchId == _originalLaunchVersionId ? string.Empty : typedLaunchId;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
