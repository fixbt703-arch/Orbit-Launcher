using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class ModBrowserWindow : Window
{
    private readonly GameInstance _instance;
    private readonly ObservableCollection<DownloadItem> _downloads;
    private readonly ModrinthService _modrinth;
    private readonly CurseForgeService _curseForge;
    private readonly ImageCacheService _imageCache;
    private readonly string _dependencyDownloadMode;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailsCancellation;
    private CancellationTokenSource? _searchDelayCancellation;
    private bool _suppressFilterSearch;
    private int _totalHits;
    private ModProjectDetails? _selectedDetails;
    private string _lastDownloadPath = string.Empty;

    public ObservableCollection<ModSearchResult> Results { get; } = [];
    public ObservableCollection<ModCategoryOption> Categories { get; } = [];
    public ObservableCollection<ModSearchOption> GameVersions { get; } = [];
    public ObservableCollection<ModSearchOption> Loaders { get; } = [];
    public IReadOnlyList<ModSearchOption> CatalogSources { get; } =
    [
        new("modrinth", "Modrinth"),
        new("curseforge", "CurseForge")
    ];
    public IReadOnlyList<ModSearchOption> ProjectTypes { get; } =
    [
        new("mod", "Моды"),
        new("shader", "Шейдеры"),
        new("resourcepack", "Ресурспаки"),
        new("modpack", "Модпаки"),
        new("datapack", "Датапаки")
    ];
    public IReadOnlyList<ModSearchOption> SortOptions { get; } =
    [
        new("relevance", "По релевантности"),
        new("downloads", "По загрузкам"),
        new("follows", "По популярности"),
        new("updated", "Недавно обновлённые"),
        new("newest", "Сначала новые")
    ];
    public IReadOnlyList<ModSearchOption> EnvironmentOptions { get; } =
    [
        new("all", "Любая сторона"),
        new("client", "Клиент"),
        new("server", "Сервер")
    ];

    public ModBrowserWindow(
        HttpClient httpClient,
        GameInstance instance,
        ObservableCollection<DownloadItem> downloads,
        string? modrinthApiBase = null,
        string? dependencyDownloadMode = null,
        string? curseForgeApiBase = null,
        string? curseForgeApiKey = null)
    {
        InitializeComponent();
        _instance = instance;
        _downloads = downloads;
        _modrinth = new ModrinthService(httpClient, modrinthApiBase);
        _curseForge = new CurseForgeService(httpClient, curseForgeApiBase, curseForgeApiKey);
        _imageCache = new ImageCacheService(httpClient);
        _dependencyDownloadMode = dependencyDownloadMode is "all" or "none" ? dependencyDownloadMode : "required";
        DataContext = this;
        InstanceText.Text = LocalizationService.IsEnglish ? $"Installing to: {instance.Name}" : $"Установка в: {instance.Name}";
        LocalizationService.Apply(this);
        Loaded += async (_, _) =>
        {
            await InitializeCatalogAsync();
            LocalizationService.Apply(this);
        };
        Closed += (_, _) =>
        {
            _searchCancellation?.Cancel();
            _detailsCancellation?.Cancel();
            _searchDelayCancellation?.Cancel();
        };
    }

    private async Task InitializeCatalogAsync()
    {
        _suppressFilterSearch = true;
        CatalogSourceCombo.SelectedValue = "modrinth";
        ProjectTypeCombo.SelectedValue = "mod";
        SortCombo.SelectedValue = "relevance";
        EnvironmentCombo.SelectedValue = "all";
        await ReloadGameVersionsAsync();
        GameVersionCombo.SelectedValue = GameVersions.Any(item => item.Id == _instance.GameVersion) ? _instance.GameVersion : string.Empty;
        await ReloadTypeFiltersAsync("mod");
        _suppressFilterSearch = false;
        await SearchAsync();
    }

    private async Task ReloadGameVersionsAsync()
    {
        GameVersions.Clear();
        GameVersions.Add(new ModSearchOption(string.Empty, "Все версии Minecraft"));
        try
        {
            var versions = IsCurseForge
                ? await _curseForge.GetGameVersionsAsync()
                : await _modrinth.GetGameVersionsAsync();
            foreach (var version in versions)
                GameVersions.Add(version);
        }
        catch
        {
            if (!GameVersions.Any(item => item.Id == _instance.GameVersion))
                GameVersions.Add(new ModSearchOption(_instance.GameVersion, _instance.GameVersion));
        }
    }

    private async Task ReloadTypeFiltersAsync(string projectType)
    {
        var previousLoader = (LoaderCombo.SelectedItem as ModSearchOption)?.Id;
        Loaders.Clear();
        Categories.Clear();
        Categories.Add(new ModCategoryOption(string.Empty, "Все категории"));

        try
        {
            var loaderOptions = IsCurseForge
                ? await _curseForge.GetLoadersAsync(projectType)
                : await _modrinth.GetLoadersAsync(projectType);
            if (projectType != "datapack" || IsCurseForge)
                Loaders.Add(new ModSearchOption(string.Empty, "Все загрузчики"));
            foreach (var loader in loaderOptions)
                Loaders.Add(loader);
        }
        catch
        {
            Loaders.Add(new ModSearchOption(string.Empty, "Все загрузчики"));
        }

        try
        {
            var categories = IsCurseForge
                ? await _curseForge.GetCategoriesAsync(projectType)
                : await _modrinth.GetCategoriesAsync(projectType);
            foreach (var category in categories)
                Categories.Add(category);
        }
        catch
        {
            // Основной поиск не зависит от списка категорий.
        }

        var preferredLoader = projectType == "mod" && !string.Equals(_instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase)
            ? _instance.Loader.ToLowerInvariant()
            : previousLoader;
        LoaderCombo.SelectedValue = Loaders.Any(item => item.Id == preferredLoader) ? preferredLoader : Loaders.FirstOrDefault()?.Id;
        CategoryCombo.SelectedIndex = 0;
        EnvironmentCombo.IsEnabled = !IsCurseForge && projectType is "mod" or "datapack";
    }

    private ModrinthSearchRequest CurrentRequest(int offset = 0) => new(
        SearchBox.Text.Trim(),
        (ProjectTypeCombo.SelectedItem as ModSearchOption)?.Id ?? "mod",
        (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty,
        (LoaderCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty,
        (CategoryCombo.SelectedItem as ModCategoryOption)?.Id ?? string.Empty,
        (EnvironmentCombo.SelectedItem as ModSearchOption)?.Id ?? "all",
        (SortCombo.SelectedItem as ModSearchOption)?.Id ?? "relevance",
        offset,
        30);

    private async Task SearchAsync(bool append = false)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        EmptyTitleText.Text = LocalizationService.Translate("Ничего не найдено");
        EmptyHintText.Text = LocalizationService.Translate("Измените запрос или фильтры.");
        if (!append)
        {
            Results.Clear();
            CloseDetails();
        }
        StatusText.Text = LocalizationService.Translate("Поиск совместимого контента…");

        try
        {
            if (IsCurseForge && !_curseForge.IsConfigured)
            {
                ShowCurseForgeSetupRequired();
                return;
            }
            var response = IsCurseForge
                ? await _curseForge.SearchAsync(CurrentRequest(append ? Results.Count : 0), token)
                : await _modrinth.SearchAsync(CurrentRequest(append ? Results.Count : 0), token);
            _totalHits = response.TotalHits;
            foreach (var project in response.Items)
                Results.Add(project);
            EmptyPanel.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LoadMoreButton.Visibility = Results.Count < _totalHits ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = LocalizationService.IsEnglish
                ? $"{CurrentSourceName}: showing {Results.Count} of {_totalHits}"
                : $"{CurrentSourceName}: показано {Results.Count} из {_totalHits}";
            _ = LoadIconsAsync(response.Items, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            LoadMoreButton.Visibility = Visibility.Collapsed;
            StatusText.Text = LocalizationService.Translate("Не удалось выполнить поиск");
            LocalizationService.Show(this, ex.Message, "Каталог контента", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadIconsAsync(IReadOnlyList<ModSearchResult> projects, CancellationToken cancellationToken)
    {
        using var limiter = new SemaphoreSlim(6, 6);
        var tasks = projects.Where(project => !string.IsNullOrWhiteSpace(project.IconUrl)).Select(async project =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                project.Icon = await _imageCache.GetAsync(project.IconUrl, 96, cancellationToken);
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
    }

    private async void ProjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressFilterSearch || ProjectTypeCombo.SelectedItem is not ModSearchOption option)
            return;
        _suppressFilterSearch = true;
        try
        {
            await ReloadTypeFiltersAsync(option.Id);
        }
        finally
        {
            _suppressFilterSearch = false;
        }
        await SearchAsync();
    }

    private async void CatalogSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressFilterSearch)
            return;
        _searchCancellation?.Cancel();
        _detailsCancellation?.Cancel();
        _suppressFilterSearch = true;
        try
        {
            var gameVersion = (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? _instance.GameVersion;
            await ReloadGameVersionsAsync();
            GameVersionCombo.SelectedValue = GameVersions.Any(item => item.Id == gameVersion) ? gameVersion
                : GameVersions.Any(item => item.Id == _instance.GameVersion) ? _instance.GameVersion : string.Empty;
            await ReloadTypeFiltersAsync((ProjectTypeCombo.SelectedItem as ModSearchOption)?.Id ?? "mod");
        }
        finally
        {
            _suppressFilterSearch = false;
        }
        await SearchAsync();
    }

    private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressFilterSearch && IsLoaded && FiltersReady())
            await SearchAsync();
    }

    private bool FiltersReady() => ProjectTypeCombo.SelectedItem is not null
                                   && GameVersionCombo.SelectedItem is not null
                                   && LoaderCombo.SelectedItem is not null
                                   && CategoryCombo.SelectedItem is not null
                                   && SortCombo.SelectedItem is not null
                                   && EnvironmentCombo.SelectedItem is not null;

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();
    private async void LoadMoreButton_Click(object sender, RoutedEventArgs e) => await SearchAsync(append: true);

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _suppressFilterSearch)
            return;
        _searchDelayCancellation?.Cancel();
        _searchDelayCancellation?.Dispose();
        _searchDelayCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(450, _searchDelayCancellation.Token);
            await SearchAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchDelayCancellation?.Cancel();
            await SearchAsync();
        }
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilterSearch = true;
        SearchBox.Clear();
        ProjectTypeCombo.SelectedValue = "mod";
        GameVersionCombo.SelectedValue = GameVersions.Any(item => item.Id == _instance.GameVersion) ? _instance.GameVersion : string.Empty;
        SortCombo.SelectedValue = "relevance";
        EnvironmentCombo.SelectedValue = "all";
        await ReloadTypeFiltersAsync("mod");
        _suppressFilterSearch = false;
        await SearchAsync();
    }

    private async void ProjectCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModSearchResult project)
            await ShowDetailsAsync(project);
    }

    private async Task ShowDetailsAsync(ModSearchResult project)
    {
        _detailsCancellation?.Cancel();
        _detailsCancellation?.Dispose();
        _detailsCancellation = new CancellationTokenSource();
        var token = _detailsCancellation.Token;
        DetailColumn.Width = new GridLength(440);
        DetailPanel.Visibility = Visibility.Visible;
        DetailLoadingPanel.Visibility = Visibility.Visible;
        DetailContent.Visibility = Visibility.Collapsed;
        DetailIcon.Source = project.Icon;
        try
        {
            var gameVersion = (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
            var loader = (LoaderCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
            var details = project.SourceId == "curseforge"
                ? await _curseForge.GetProjectDetailsAsync(project, gameVersion, loader, token)
                : await _modrinth.GetProjectDetailsAsync(project, gameVersion, loader, token);
            _selectedDetails = details;
            DetailContent.DataContext = details;
            DetailInstallButton.Tag = project;
            DetailContent.Visibility = Visibility.Visible;
            DetailLoadingPanel.Visibility = Visibility.Collapsed;
            _ = LoadGalleryAsync(details.Gallery.Take(10).ToList(), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DetailLoadingPanel.Visibility = Visibility.Collapsed;
            CloseDetails();
            LocalizationService.Show(this, ex.Message, "Сведения о проекте", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task LoadGalleryAsync(IReadOnlyList<ModGalleryImage> images, CancellationToken cancellationToken)
    {
        using var limiter = new SemaphoreSlim(4, 4);
        var tasks = images.Select(async galleryImage =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                galleryImage.Image = await _imageCache.GetAsync(galleryImage.Url, 760, cancellationToken);
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
    }

    private void CloseDetailsButton_Click(object sender, RoutedEventArgs e) => CloseDetails();

    private void CloseDetails()
    {
        _detailsCancellation?.Cancel();
        _selectedDetails = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        DetailContent.DataContext = null;
        OpenDownloadFolderButton.Visibility = Visibility.Collapsed;
        _lastDownloadPath = string.Empty;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Button)?.Tag is ModSearchResult project && sender is Button button)
            await DownloadProjectAsync(project, button);
    }

    private async Task DownloadProjectAsync(ModSearchResult project, Button button)
    {
        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = LocalizationService.Translate("Загрузка…");
        var download = new DownloadItem { Title = project.Title, Subtitle = $"{project.ProjectTypeDisplay} → {_instance.Name}", Status = LocalizationService.Translate("Загрузка") };
        _downloads.Insert(0, download);
        var progress = new Progress<double>(value =>
        {
            download.Progress = Math.Clamp(value, 0, 100);
            download.Status = $"{value:0}%";
        });

        try
        {
            var gameVersion = (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
            var loader = (LoaderCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
            var result = project.SourceId == "curseforge"
                ? await _curseForge.DownloadAsync(project, _instance, gameVersion, loader, _dependencyDownloadMode, progress)
                : await _modrinth.DownloadAsync(project, _instance, gameVersion, loader, _dependencyDownloadMode, progress);
            download.Progress = 100;
            download.Status = LocalizationService.Translate(result.InstalledDirectly ? "Установлен" : "Скачан");
            button.Content = LocalizationService.Translate(result.InstalledDirectly ? "Установлен" : "Скачан");
            StatusText.Text = result.Message;
            _lastDownloadPath = result.DestinationPath;
            OpenDownloadFolderButton.Visibility = DetailPanel.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            download.Status = LocalizationService.Translate("Ошибка");
            button.Content = originalContent;
            button.IsEnabled = true;
            LocalizationService.Show(this, $"Не удалось скачать проект.\n\n{ex.Message}", "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenProjectWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDetails is null)
            return;
        var url = _selectedDetails.WebsiteUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            var slug = string.IsNullOrWhiteSpace(_selectedDetails.Slug) ? _selectedDetails.ProjectId : _selectedDetails.Slug;
            url = _selectedDetails.SearchResult.SourceId == "curseforge"
                ? $"https://www.curseforge.com/minecraft/mc-mods/{slug}"
                : $"https://modrinth.com/project/{slug}";
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastDownloadPath))
            return;
        var directory = Path.GetDirectoryName(_lastDownloadPath);
        if (directory is not null)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private bool IsCurseForge => (CatalogSourceCombo.SelectedItem as ModSearchOption)?.Id == "curseforge";
    private string CurrentSourceName => IsCurseForge ? "CurseForge" : "Modrinth";

    private void ShowCurseForgeSetupRequired()
    {
        Results.Clear();
        CloseDetails();
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        EmptyTitleText.Text = LocalizationService.Translate("Нужен ключ CurseForge API");
        EmptyHintText.Text = LocalizationService.Translate("Ключ не включён в эту сборку Orbit Launcher. Обратитесь к автору лаунчера.");
        LoadMoreButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "CurseForge не настроен";
    }
}
