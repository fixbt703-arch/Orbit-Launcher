using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private IReadOnlyList<MinecraftVersionInfo> _versions = [];
    private CancellationTokenSource? _loaderVersionsCancellation;

    public ObservableCollection<MinecraftVersionInfo> VisibleVersions { get; } = [];
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];
    public GameInstance? Result { get; private set; }
    public string? SelectedIconPath { get; private set; }

    public CreateInstanceDialog(HttpClient httpClient, LauncherSettings settings)
    {
        InitializeComponent();
        _versionService = new VersionManifestService(httpClient, settings.MinecraftManifestApi);
        _loaderVersionService = new LoaderVersionService(httpClient, settings);
        _settings = settings;
        MinRamBox.Text = settings.DefaultMinimumRamMb.ToString();
        MaxRamBox.Text = settings.DefaultMaximumRamMb.ToString();
        DataContext = this;
        LocalizationService.Apply(this);
        Loaded += CreateInstanceDialog_Loaded;
        Closed += (_, _) => _loaderVersionsCancellation?.Cancel();
    }

    private async void CreateInstanceDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = false;
        try
        {
            _versions = await _versionService.GetVersionsAsync(_settings.ShowSnapshots);
            VersionLoadingText.Text = LocalizationService.IsEnglish ? $"{_versions.Count} versions" : $"{_versions.Count} версий";
        }
        catch
        {
            _versions = new[] { "1.21.8", "1.21.7", "1.21.6", "1.21.5", "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.16.5" }
                .Select(id => new MinecraftVersionInfo { Id = id, ReleaseTime = DateTime.Today })
                .ToList();
            VersionLoadingText.Text = LocalizationService.Translate("Резервный список");
        }

        ApplyVersionFilter();
        VersionList.SelectedIndex = 0;
        CreateButton.IsEnabled = true;
        LocalizationService.Apply(this);
    }

    private void VersionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
            ApplyVersionFilter();
    }

    private async void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await RefreshLoaderVersionsAsync();
    }

    private async void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            await RefreshLoaderVersionsAsync();
    }

    private string SelectedLoader => new[] { VanillaLoader, FabricLoader, ForgeLoader, NeoForgeLoader, QuiltLoader }
        .FirstOrDefault(button => button.IsChecked == true)?.Tag?.ToString() ?? "Vanilla";

    private async Task RefreshLoaderVersionsAsync()
    {
        _loaderVersionsCancellation?.Cancel();
        _loaderVersionsCancellation?.Dispose();
        _loaderVersionsCancellation = new CancellationTokenSource();
        LoaderVersions.Clear();
        LoaderVersionCombo.IsEnabled = false;

        var loader = SelectedLoader;
        var gameVersion = (VersionList.SelectedItem as MinecraftVersionInfo)?.Id ?? string.Empty;
        if (loader == "Vanilla")
        {
            LoaderVersionStatusText.Text = LocalizationService.Translate("Для Vanilla не требуется");
            return;
        }

        LoaderVersionStatusText.Text = LocalizationService.Translate("Загрузка версий…");
        try
        {
            var versions = await _loaderVersionService.GetVersionsAsync(loader, gameVersion, _loaderVersionsCancellation.Token);
            foreach (var version in versions)
                LoaderVersions.Add(version);
            LoaderVersionCombo.SelectedIndex = LoaderVersions.Count > 0 ? 0 : -1;
            LoaderVersionCombo.IsEnabled = LoaderVersions.Count > 0;
            LoaderVersionStatusText.Text = LoaderVersions.Count > 0 ? $"Доступно: {LoaderVersions.Count}" : "Нет совместимых версий";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            LoaderVersionStatusText.Text = LocalizationService.Translate("Не удалось получить список");
        }
    }

    private void ApplyVersionFilter()
    {
        var query = VersionSearchBox.Text.Trim();
        var selectedId = (VersionList.SelectedItem as MinecraftVersionInfo)?.Id;
        VisibleVersions.Clear();
        foreach (var version in _versions.Where(version => string.IsNullOrWhiteSpace(query)
                                                           || version.Id.Contains(query, StringComparison.OrdinalIgnoreCase)))
            VisibleVersions.Add(version);

        VersionList.SelectedItem = VisibleVersions.FirstOrDefault(version => version.Id == selectedId)
                                   ?? VisibleVersions.FirstOrDefault();
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (picker.ShowDialog(this) != true)
            return;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(picker.FileName, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            IconPreview.Source = image;
            DefaultIconGlyph.Visibility = Visibility.Collapsed;
            SelectedIconPath = picker.FileName;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(this, $"Не удалось открыть изображение.\n\n{ex.Message}", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var gameVersion = (VersionList.SelectedItem as MinecraftVersionInfo)?.Id ?? string.Empty;
        var loader = SelectedLoader;
        var loaderVersion = (LoaderVersionCombo.SelectedItem as LoaderVersionInfo)?.Id ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(gameVersion))
        {
            LocalizationService.Show(this, "Укажите название и выберите версию Minecraft.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (loader != "Vanilla" && string.IsNullOrWhiteSpace(loaderVersion))
        {
            LocalizationService.Show(this, "Выберите версию загрузчика. Если список пуст, эта версия Minecraft не поддерживается выбранным загрузчиком.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MinRamBox.Text, out var minRam) || !int.TryParse(MaxRamBox.Text, out var maxRam)
            || minRam < 512 || maxRam < minRam || maxRam > 65536)
        {
            LocalizationService.Show(this, "Проверьте объём памяти: минимум 512 МБ, максимум не меньше минимума.", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new GameInstance
        {
            Name = name,
            GameVersion = gameVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            MinimumRamMb = minRam,
            MaximumRamMb = maxRam
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
