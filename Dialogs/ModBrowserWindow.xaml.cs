using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using OrbitLauncher.Controls;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class ModBrowserWindow : Window
{
	private sealed record LocalCatalogFile(string Path, CatalogInstallState State);

	private sealed record DescriptionImageSpec(string ImageUrl, string LinkUrl, string AltText, double? Width, double? Height);

	private sealed class DescriptionImageItem(DescriptionImageSpec spec, AnimatedGifImage image, Border container)
	{
		public DescriptionImageSpec Spec { get; } = spec;

		public AnimatedGifImage Image { get; } = image;

		public Border Container { get; } = container;
	}

	private sealed class DescriptionImageRow(StackPanel host)
	{
		public StackPanel Host { get; } = host;

		public List<DescriptionImageItem> Items { get; } = new List<DescriptionImageItem>();
	}

	private readonly GameInstance _instance;

	private readonly ObservableCollection<DownloadItem> _downloads;

	private readonly ModrinthService _modrinth;

	private readonly CurseForgeService _curseForge;

	private readonly ImageCacheService _imageCache;

	private readonly string _dependencyDownloadMode;

	private readonly InstalledContentRegistry _installedContentRegistry = new InstalledContentRegistry();

	private IReadOnlyList<InstalledContentRecord> _installedContentRecords = Array.Empty<InstalledContentRecord>();

	private readonly Dictionary<string, CatalogInstallState> _knownInstallStates = new Dictionary<string, CatalogInstallState>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, CatalogInstallState> _detectedModrinthProjects = new Dictionary<string, CatalogInstallState>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<ModSearchResult> _requestedIcons = new HashSet<ModSearchResult>();

	private readonly Dictionary<ModSearchResult, int> _iconAttempts = new Dictionary<ModSearchResult, int>();

	private readonly SemaphoreSlim _iconLimiter = new SemaphoreSlim(3, 3);

	private CancellationTokenSource? _searchCancellation;

	private CancellationTokenSource? _imageCancellation;

	private CancellationTokenSource? _detailsCancellation;

	private CancellationTokenSource? _searchDelayCancellation;

	private readonly CancellationTokenSource _windowCancellation = new CancellationTokenSource();

	private bool _closed;

	private bool _suppressFilterSearch;

	private int _activeInstalls;

	private bool _installingModpack;

	private int _totalHits;

	private ModProjectDetails? _selectedDetails;

	private string _lastDownloadPath = string.Empty;


	public ObservableCollection<ModSearchResult> Results { get; } = new ObservableCollection<ModSearchResult>();

	public ObservableCollection<ModCategoryOption> Categories { get; } = new ObservableCollection<ModCategoryOption>();

	public ObservableCollection<ModSearchOption> GameVersions { get; } = new ObservableCollection<ModSearchOption>();

	public ObservableCollection<ModSearchOption> Loaders { get; } = new ObservableCollection<ModSearchOption>();

	public IReadOnlyList<ModSearchOption> CatalogSources { get; } = new global::_003C_003Ez__ReadOnlyArray<ModSearchOption>(new ModSearchOption[2]
	{
		new ModSearchOption("modrinth", "Modrinth"),
		new ModSearchOption("curseforge", "CurseForge")
	});

	public IReadOnlyList<ModSearchOption> ProjectTypes { get; }

	public IReadOnlyList<ModSearchOption> SortOptions { get; } = new global::_003C_003Ez__ReadOnlyArray<ModSearchOption>(new ModSearchOption[5]
	{
		new ModSearchOption("relevance", "По релевантности"),
		new ModSearchOption("downloads", "По загрузкам"),
		new ModSearchOption("follows", "По популярности"),
		new ModSearchOption("updated", "Недавно обновлённые"),
		new ModSearchOption("newest", "Сначала новые")
	});

	public IReadOnlyList<ModSearchOption> EnvironmentOptions { get; } = new global::_003C_003Ez__ReadOnlyArray<ModSearchOption>(new ModSearchOption[3]
	{
		new ModSearchOption("all", "Любая сторона"),
		new ModSearchOption("client", "Клиент"),
		new ModSearchOption("server", "Сервер")
	});

	private bool IsCurseForge => (CatalogSourceCombo.SelectedItem as ModSearchOption)?.Id == "curseforge";

	private string CurrentSourceName
	{
		get
		{
			if (!IsCurseForge)
			{
				return "Modrinth";
			}
			return "CurseForge";
		}
	}

	private bool IsVanillaInstance => string.Equals(_instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase);

	private string DefaultProjectType
	{
		get
		{
			if (!IsVanillaInstance)
			{
				return "mod";
			}
			return "resourcepack";
		}
	}

	public ModBrowserWindow(HttpClient httpClient, GameInstance instance, ObservableCollection<DownloadItem> downloads, string? modrinthApiBase = null, string? dependencyDownloadMode = null, string? curseForgeApiBase = null, string? curseForgeApiKey = null)
	{
		InitializeComponent();
		_instance = instance;
		_downloads = downloads;
		_modrinth = new ModrinthService(httpClient, modrinthApiBase);
		_curseForge = new CurseForgeService(httpClient, curseForgeApiBase, curseForgeApiKey);
		_imageCache = new ImageCacheService(httpClient);
		_dependencyDownloadMode = (((dependencyDownloadMode == "all" || dependencyDownloadMode == "none") ? true : false) ? dependencyDownloadMode : "required");
		_installedContentRecords = _installedContentRegistry.Load(instance);
		IReadOnlyList<ModSearchOption> readOnlyList2;
		if (!IsVanillaInstance)
		{
			IReadOnlyList<ModSearchOption> readOnlyList = new global::_003C_003Ez__ReadOnlyArray<ModSearchOption>(new ModSearchOption[5]
			{
				new ModSearchOption("mod", "Моды"),
				new ModSearchOption("shader", "Шейдеры"),
				new ModSearchOption("resourcepack", "Ресурспаки"),
				new ModSearchOption("modpack", "Модпаки"),
				new ModSearchOption("datapack", "Датапаки")
			});
			readOnlyList2 = readOnlyList;
		}
		else
		{
			IReadOnlyList<ModSearchOption> readOnlyList = new global::_003C_003Ez__ReadOnlyArray<ModSearchOption>(new ModSearchOption[2]
			{
				new ModSearchOption("resourcepack", "Ресурспаки"),
				new ModSearchOption("datapack", "Датапаки")
			});
			readOnlyList2 = readOnlyList;
		}
		ProjectTypes = readOnlyList2;
		base.DataContext = this;
		InstanceText.Text = (LocalizationService.IsEnglish ? ("Installing to: " + instance.Name) : ("Установка в: " + instance.Name));
		LocalizationService.Apply(this);
		base.Loaded += async delegate
		{
			try
			{
				await InitializeCatalogAsync(_windowCancellation.Token);
				if (!_closed)
				{
					LocalizationService.Apply(this);
				}
			}
			catch (OperationCanceledException)
			{
			}
		};
		base.Closed += delegate
		{
			StopBackgroundWork();
		};
	}

	private async Task InitializeCatalogAsync(CancellationToken cancellationToken)
	{
		_suppressFilterSearch = true;
		CatalogSourceCombo.SelectedValue = "modrinth";
		ProjectTypeCombo.SelectedValue = DefaultProjectType;
		SortCombo.SelectedValue = "relevance";
		EnvironmentCombo.SelectedValue = "all";
		await ReloadGameVersionsAsync(cancellationToken);
		GameVersionCombo.SelectedValue = (GameVersions.Any((ModSearchOption item) => item.Id == _instance.GameVersion) ? _instance.GameVersion : string.Empty);
		await ReloadTypeFiltersAsync(DefaultProjectType, cancellationToken);
		await DetectExistingModrinthContentAsync(cancellationToken);
		_suppressFilterSearch = false;
		await SearchAsync();
	}

	private async Task ReloadGameVersionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		GameVersions.Clear();
		GameVersions.Add(new ModSearchOption(string.Empty, "Все версии Minecraft"));
		try
		{
			IReadOnlyList<ModSearchOption> readOnlyList = ((!IsCurseForge) ? (await _modrinth.GetGameVersionsAsync(cancellationToken)) : (await _curseForge.GetGameVersionsAsync(cancellationToken)));
			foreach (ModSearchOption item in readOnlyList)
			{
				GameVersions.Add(item);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
			if (!GameVersions.Any((ModSearchOption item) => item.Id == _instance.GameVersion))
			{
				GameVersions.Add(new ModSearchOption(_instance.GameVersion, _instance.GameVersion));
			}
		}
	}

	private async Task ReloadTypeFiltersAsync(string projectType, CancellationToken cancellationToken = default(CancellationToken))
	{
		string? previousLoader = (LoaderCombo.SelectedItem as ModSearchOption)?.Id;
		Loaders.Clear();
		Categories.Clear();
		Categories.Add(new ModCategoryOption(string.Empty, "Все категории"));
		try
		{
			IReadOnlyList<ModSearchOption> readOnlyList = ((!IsCurseForge) ? (await _modrinth.GetLoadersAsync(projectType, cancellationToken)) : (await _curseForge.GetLoadersAsync(projectType, cancellationToken)));
			if (projectType != "datapack" || IsCurseForge)
			{
				Loaders.Add(new ModSearchOption(string.Empty, "Все загрузчики"));
			}
			foreach (ModSearchOption item in readOnlyList)
			{
				Loaders.Add(item);
			}
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
			Loaders.Add(new ModSearchOption(string.Empty, "Все загрузчики"));
		}
		try
		{
			IReadOnlyList<ModCategoryOption> readOnlyList2 = ((!IsCurseForge) ? (await _modrinth.GetCategoriesAsync(projectType, cancellationToken)) : (await _curseForge.GetCategoriesAsync(projectType, cancellationToken)));
			foreach (ModCategoryOption item2 in readOnlyList2)
			{
				Categories.Add(item2);
			}
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
		}
		string? preferredLoader = ((projectType == "mod" && !string.Equals(_instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase)) ? _instance.Loader.ToLowerInvariant() : previousLoader);
		LoaderCombo.SelectedValue = (Loaders.Any((ModSearchOption item) => item.Id == preferredLoader) ? preferredLoader : Loaders.FirstOrDefault()?.Id);
		CategoryCombo.SelectedIndex = 0;
		ComboBox environmentCombo = EnvironmentCombo;
		bool flag = !IsCurseForge;
		if (flag)
		{
			bool flag2 = ((projectType == "mod" || projectType == "datapack") ? true : false);
			flag = flag2;
		}
		environmentCombo.IsEnabled = flag;
	}

	private ModrinthSearchRequest CurrentRequest(int offset = 0)
	{
		return new ModrinthSearchRequest(SearchBox.Text.Trim(), (ProjectTypeCombo.SelectedItem as ModSearchOption)?.Id ?? DefaultProjectType, (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty, (LoaderCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty, (CategoryCombo.SelectedItem as ModCategoryOption)?.Id ?? string.Empty, (EnvironmentCombo.SelectedItem as ModSearchOption)?.Id ?? "all", (SortCombo.SelectedItem as ModSearchOption)?.Id ?? "relevance", offset, 30);
	}

	private async Task SearchAsync(bool append = false)
	{
		if (_closed)
		{
			return;
		}
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		_searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowCancellation.Token);
		CancellationToken token = _searchCancellation.Token;
		if (!append || _imageCancellation == null)
		{
			_imageCancellation?.Cancel();
			_imageCancellation?.Dispose();
			_imageCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowCancellation.Token);
			_requestedIcons.Clear();
			_iconAttempts.Clear();
		}
		LoadingPanel.Visibility = Visibility.Visible;
		LoadMoreButton.IsEnabled = false;
		EmptyPanel.Visibility = Visibility.Collapsed;
		EmptyTitleText.Text = LocalizationService.Translate("Ничего не найдено");
		EmptyHintText.Text = LocalizationService.Translate("Измените запрос или фильтры.");
		if (!append)
		{
			Results.Clear();
			CloseDetails();
			ResultsScroll.ScrollToTop();
		}
		StatusText.Text = LocalizationService.Translate("Поиск совместимого контента…");
		try
		{
			if (IsCurseForge && !_curseForge.IsConfigured)
			{
				ShowCurseForgeSetupRequired();
				return;
			}
			ModrinthSearchResponse modrinthSearchResponse = ((!IsCurseForge) ? (await _modrinth.SearchAsync(CurrentRequest(append ? Results.Count : 0), token)) : (await _curseForge.SearchAsync(CurrentRequest(append ? Results.Count : 0), token)));
			ModrinthSearchResponse modrinthSearchResponse2 = modrinthSearchResponse;
			token.ThrowIfCancellationRequested();
			_totalHits = modrinthSearchResponse2.TotalHits;
			foreach (ModSearchResult item in modrinthSearchResponse2.Items)
			{
				ApplyKnownInstallState(item);
				Results.Add(item);
			}
			EmptyPanel.Visibility = ((Results.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
			LoadMoreButton.Visibility = ((Results.Count >= _totalHits) ? Visibility.Collapsed : Visibility.Visible);
			StatusText.Text = (LocalizationService.IsEnglish ? $"{CurrentSourceName}: showing {Results.Count} of {_totalHits}" : $"{CurrentSourceName}: показано {Results.Count} из {_totalHits}");
			ScheduleVisibleIcons();
			_ = base.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScheduleVisibleIcons));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			EmptyPanel.Visibility = Visibility.Visible;
			LoadMoreButton.Visibility = Visibility.Collapsed;
			StatusText.Text = LocalizationService.Translate("Не удалось выполнить поиск");
			LocalizationService.Show(this, ex2.Message, "Каталог контента", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			if (!_closed && !token.IsCancellationRequested)
			{
				LoadingPanel.Visibility = Visibility.Collapsed;
				LoadMoreButton.IsEnabled = true;
			}
		}
	}

	private void ResultsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (e.VerticalChange != 0.0 || e.ViewportHeightChange != 0.0 || e.ExtentHeightChange != 0.0)
		{
			ScheduleVisibleIcons();
		}
	}

	private void ScheduleVisibleIcons()
	{
		if (!_closed && _imageCancellation != null && Results.Count != 0)
		{
			// Measure the actual cards: their height depends on layout, text and window size.
			// A guessed row height skips visible projects after scrolling or appending pages.
			var viewport = new Rect(0, -200, Math.Max(1, ResultsScroll.ActualWidth), ResultsScroll.ViewportHeight + 400);
			var visible = new List<ModSearchResult>();
			for (int i = 0; i < Results.Count; i++)
			{
				if (ResultsItems.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement card || !card.IsArrangeValid)
					continue;
				var bounds = card.TransformToAncestor(ResultsScroll).TransformBounds(new Rect(card.RenderSize));
				if (bounds.Top > viewport.Bottom)
					break;
				var project = Results[i];
				if (bounds.IntersectsWith(viewport) && project.Icon == null && !string.IsNullOrWhiteSpace(project.IconUrl) && _requestedIcons.Add(project))
					visible.Add(project);
			}
			ModSearchResult[] array = visible.ToArray();
			foreach (ModSearchResult modSearchResult in array)
			{
				_iconAttempts[modSearchResult] = _iconAttempts.GetValueOrDefault(modSearchResult) + 1;
				_ = LoadIconAsync(modSearchResult, _imageCancellation.Token);
			}
		}
	}

	private async Task LoadIconAsync(ModSearchResult project, CancellationToken cancellationToken)
	{
		_ = 2;
		try
		{
			await _iconLimiter.WaitAsync(cancellationToken);
			bool retry = false;
			try
			{
				BitmapSource? bitmapSource = await _imageCache.GetAsync(project.IconUrl, 96, cancellationToken);
				if (bitmapSource != null && !cancellationToken.IsCancellationRequested && !_closed)
				{
					project.Icon = bitmapSource;
				}
				else
				{
					retry = bitmapSource == null && _iconAttempts.GetValueOrDefault(project) < 2;
				}
			}
			finally
			{
				_iconLimiter.Release();
			}
			if (retry)
			{
				await Task.Delay(1500, cancellationToken);
				_requestedIcons.Remove(project);
				ScheduleVisibleIcons();
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception)
		{
		}
	}

	private async void ProjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (base.IsLoaded && !_suppressFilterSearch && ProjectTypeCombo.SelectedItem is ModSearchOption modSearchOption)
		{
			_suppressFilterSearch = true;
			try
			{
				await ReloadTypeFiltersAsync(modSearchOption.Id, _windowCancellation.Token);
			}
			finally
			{
				_suppressFilterSearch = false;
			}
			await SearchAsync();
		}
	}

	private async void CatalogSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!base.IsLoaded || _suppressFilterSearch)
		{
			return;
		}
		_searchCancellation?.Cancel();
		_imageCancellation?.Cancel();
		_detailsCancellation?.Cancel();
		_suppressFilterSearch = true;
		try
		{
			string gameVersion = (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? _instance.GameVersion;
			await ReloadGameVersionsAsync(_windowCancellation.Token);
			GameVersionCombo.SelectedValue = (GameVersions.Any((ModSearchOption item) => item.Id == gameVersion) ? gameVersion : (GameVersions.Any((ModSearchOption item) => item.Id == _instance.GameVersion) ? _instance.GameVersion : string.Empty));
			await ReloadTypeFiltersAsync((ProjectTypeCombo.SelectedItem as ModSearchOption)?.Id ?? "mod", _windowCancellation.Token);
		}
		finally
		{
			_suppressFilterSearch = false;
		}
		await SearchAsync();
	}

	private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_suppressFilterSearch && base.IsLoaded && FiltersReady())
		{
			await SearchAsync();
		}
	}

	private bool FiltersReady()
	{
		if (ProjectTypeCombo.SelectedItem != null && GameVersionCombo.SelectedItem != null && LoaderCombo.SelectedItem != null && CategoryCombo.SelectedItem != null && SortCombo.SelectedItem != null)
		{
			return EnvironmentCombo.SelectedItem != null;
		}
		return false;
	}

	private async void SearchButton_Click(object sender, RoutedEventArgs e)
	{
		await SearchAsync();
	}

	private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
	{
		await SearchAsync(append: true);
	}

	private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!base.IsLoaded || _suppressFilterSearch)
		{
			return;
		}
		_searchDelayCancellation?.Cancel();
		_searchDelayCancellation?.Dispose();
		_searchDelayCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowCancellation.Token);
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
		if (e.Key == Key.Return)
		{
			_searchDelayCancellation?.Cancel();
			await SearchAsync();
		}
	}

	private async void ResetFilters_Click(object sender, RoutedEventArgs e)
	{
		_suppressFilterSearch = true;
		SearchBox.Clear();
		ProjectTypeCombo.SelectedValue = DefaultProjectType;
		GameVersionCombo.SelectedValue = (GameVersions.Any((ModSearchOption item) => item.Id == _instance.GameVersion) ? _instance.GameVersion : string.Empty);
		SortCombo.SelectedValue = "relevance";
		EnvironmentCombo.SelectedValue = "all";
		await ReloadTypeFiltersAsync(DefaultProjectType, _windowCancellation.Token);
		_suppressFilterSearch = false;
		await SearchAsync();
	}

	private async void ProjectCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is ModSearchResult project)
		{
			await ShowDetailsAsync(project);
		}
	}

	private async Task ShowDetailsAsync(ModSearchResult project)
	{
		if (_closed)
		{
			return;
		}
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		_detailsCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowCancellation.Token);
		CancellationToken token = _detailsCancellation.Token;
		DetailColumn.Width = new GridLength(0.0);
		ResultsPane.Visibility = Visibility.Collapsed;
		InterfaceMotion.Reveal(DetailPanel);
		DetailPanel.Visibility = Visibility.Visible;
		DetailLoadingPanel.Visibility = Visibility.Visible;
		DetailContent.Visibility = Visibility.Collapsed;
		DetailIcon.SetBinding(Image.SourceProperty, new Binding("Icon")
		{
			Source = project
		});
		DescriptionHost.Children.Clear();
		try
		{
			string gameVersion = (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
			string loader = (LoaderCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
			ModProjectDetails selectedDetails = ((!(project.SourceId == "curseforge")) ? (await _modrinth.GetProjectDetailsAsync(project, gameVersion, loader, token)) : (await _curseForge.GetProjectDetailsAsync(project, gameVersion, loader, token)));
			ModProjectDetails modProjectDetails = (_selectedDetails = selectedDetails);
			DetailContent.DataContext = modProjectDetails;
			DetailContent.Visibility = Visibility.Visible;
			DetailLoadingPanel.Visibility = Visibility.Collapsed;
			InterfaceMotion.Reveal(DetailContent);
			_ = LoadGalleryAsync(modProjectDetails.Gallery.Take(10).ToList(), token);
			_ = RenderDescriptionAsync(modProjectDetails.BodyText, token);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			DetailLoadingPanel.Visibility = Visibility.Collapsed;
			CloseDetails();
			LocalizationService.Show(this, ex2.Message, "Сведения о проекте", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task LoadGalleryAsync(IReadOnlyList<ModGalleryImage> images, CancellationToken cancellationToken)
	{
		SemaphoreSlim limiter = new SemaphoreSlim(4, 4);
		try
		{
			IEnumerable<Task> tasks = ((IEnumerable<ModGalleryImage>)images).Select((Func<ModGalleryImage, Task>)async delegate(ModGalleryImage galleryImage)
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
		finally
		{
			if (limiter != null)
			{
				((IDisposable)limiter).Dispose();
			}
		}
	}

	private void CloseDetailsButton_Click(object sender, RoutedEventArgs e)
	{
		CloseDetails();
	}

	private void CloseDetails()
	{
		_detailsCancellation?.Cancel();
		_selectedDetails = null;
		DetailPanel.Visibility = Visibility.Collapsed;
		ResultsPane.Visibility = Visibility.Visible;
		InterfaceMotion.Reveal(ResultsPane);
		DetailColumn.Width = new GridLength(0.0);
		DetailContent.DataContext = null;
		BindingOperations.ClearBinding(DetailIcon, Image.SourceProperty);
		DescriptionHost.Children.Clear();
		OpenDownloadFolderButton.Visibility = Visibility.Collapsed;
		_lastDownloadPath = string.Empty;
	}

	private async void InstallButton_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if ((sender as Button)?.Tag is ModSearchResult { CanInstall: not false } modSearchResult)
		{
			await DownloadProjectAsync(modSearchResult);
		}
	}

	private async void ChooseVersion_Click(object sender, RoutedEventArgs e)
	{
		ModProjectDetails? details = _selectedDetails;
		if (details == null)
		{
			return;
		}
		VersionPickerWindow versionPickerWindow = new VersionPickerWindow(_instance, details, (string gameVersion, string loader, CancellationToken token) => (!(details.SearchResult.SourceId == "curseforge")) ? _modrinth.GetProjectVersionsAsync(details.ProjectId, gameVersion, (details.ProjectType == "datapack") ? "datapack" : loader, token) : _curseForge.GetProjectVersionsAsync(details.ProjectId, gameVersion, loader, token))
		{
			Owner = this
		};
		if (versionPickerWindow.ShowDialog() == true)
		{
			CatalogVersion? selectedVersion = versionPickerWindow.SelectedVersion;
			if (selectedVersion is not null)
			{
				await DownloadProjectAsync(details.SearchResult, selectedVersion);
			}
		}
	}

	private void GalleryImage_Click(object sender, MouseButtonEventArgs e)
	{
		ModProjectDetails? selectedDetails = _selectedDetails;
		if (selectedDetails != null && (sender as FrameworkElement)?.DataContext is ModGalleryImage item)
		{
			List<ModGalleryImage> list = selectedDetails.Gallery.ToList();
			int num = list.IndexOf(item);
			if (num >= 0)
			{
				GalleryViewerWindow galleryViewerWindow = new GalleryViewerWindow(list, num, _imageCache);
				galleryViewerWindow.Owner = this;
				galleryViewerWindow.ShowDialog();
			}
		}
	}

	private async Task DownloadProjectAsync(ModSearchResult project, CatalogVersion? selectedVersion = null)
	{
		if (_installingModpack || project.InstallState == CatalogInstallState.Downloading || (selectedVersion is null && !project.CanInstall))
		{
			return;
		}
		bool isModpack = project.SourceId == "modrinth" && project.ProjectType == "modpack";
		if (isModpack)
		{
			if (_activeInstalls > 0)
			{
				MessageBox.Show(this, LocalizationService.IsEnglish ? "Wait for the current downloads to finish." : "Дождитесь завершения текущих загрузок.", "Orbit Launcher");
				return;
			}
			string messageBoxText = (LocalizationService.IsEnglish ? "Close Minecraft before continuing. The modpack will replace this instance's mods and select its required Minecraft and loader versions. Previous mods and replaced files will be backed up; existing worlds will be kept. Install the modpack?" : "Перед продолжением закройте Minecraft. Модпак заменит моды этой сборки и установит нужные версии Minecraft и загрузчика. Старые моды и заменяемые файлы будут сохранены в резервную копию; существующие миры останутся нетронутыми. Установить модпак?");
			if (MessageBox.Show(this, messageBoxText, project.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
		}
		bool flag = IsVanillaInstance;
		if (flag)
		{
			string projectType = project.ProjectType;
			bool flag2 = ((projectType == "resourcepack" || projectType == "datapack") ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			LocalizationService.Show(this, "Для сборки без загрузчика доступны только ресурспаки и датапаки.", "Каталог контента", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		_activeInstalls++;
		_installingModpack = isModpack;
		project.SetInstallState(CatalogInstallState.Downloading);
		DownloadItem download = new DownloadItem
		{
			Title = project.Title,
			Subtitle = project.ProjectTypeDisplay + " → " + _instance.Name,
			Status = LocalizationService.Translate("Загрузка")
		};
		_downloads.Insert(0, download);
		Progress<double> progress = new Progress<double>(delegate(double value)
		{
			download.Progress = Math.Clamp(value, 0.0, 100.0);
			download.Status = $"{value:0}%";
		});
		try
		{
			string selectedGameVersion = (GameVersionCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
			string selectedLoader = (LoaderCombo.SelectedItem as ModSearchOption)?.Id ?? string.Empty;
			InstalledContentRecord? previousRecord = ((selectedVersion is null || project.ProjectType == "modpack") ? null : _installedContentRecords.FirstOrDefault((InstalledContentRecord record) => record.SourceId.Equals(project.SourceId, StringComparison.OrdinalIgnoreCase) && record.ProjectId.Equals(project.ProjectId, StringComparison.OrdinalIgnoreCase)));
			ModDownloadResult modDownloadResult;
			if (project.SourceId == "curseforge")
			{
				CurseForgeService curseForge = _curseForge;
				ModSearchResult project2 = project;
				GameInstance instance = _instance;
				string dependencyDownloadMode = _dependencyDownloadMode;
				int? selectedFileId = ((selectedVersion is null) ? ((int?)null) : new int?(int.Parse(selectedVersion.Id)));
				modDownloadResult = await curseForge.DownloadAsync(project2, instance, selectedGameVersion, selectedLoader, dependencyDownloadMode, progress, default(CancellationToken), selectedFileId);
			}
			else
			{
				ModrinthService modrinth = _modrinth;
				ModSearchResult project3 = project;
				GameInstance instance2 = _instance;
				string dependencyDownloadMode2 = _dependencyDownloadMode;
				string? projectType = selectedVersion?.Id;
				modDownloadResult = await modrinth.DownloadAsync(project3, instance2, selectedGameVersion, selectedLoader, dependencyDownloadMode2, progress, default(CancellationToken), projectType);
			}
			ModDownloadResult result = modDownloadResult;
			bool previousVersionBackedUp = previousRecord == null || BackupPreviousVersion(previousRecord, result.DestinationPath);
			download.Progress = 100.0;
			download.Status = LocalizationService.Translate(result.InstalledDirectly ? "Установлен" : "Скачан");
			CatalogInstallState catalogInstallState = (result.InstalledDirectly ? CatalogInstallState.Installed : CatalogInstallState.Downloaded);
			project.SetInstallState(catalogInstallState);
			RememberInstallState(project, catalogInstallState);
			try
			{
				await _installedContentRegistry.MarkAsync(_instance, project, catalogInstallState, result.DestinationPath);
				_installedContentRecords = _installedContentRegistry.Load(_instance);
			}
			catch (Exception)
			{
			}
			StatusText.Text = (previousVersionBackedUp ? result.Message : (result.Message + " Предыдущий файл не удалось убрать: проверьте папку сборки перед запуском."));
			_lastDownloadPath = result.DestinationPath;
			OpenDownloadFolderButton.Visibility = ((DetailPanel.Visibility != Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible);
			if (isModpack)
			{
				MessageBox.Show(this, result.Message, project.Title, MessageBoxButton.OK, MessageBoxImage.Asterisk);
				_installingModpack = false;
				Close();
			}
		}
		catch (Exception ex2)
		{
			download.Status = LocalizationService.Translate("Ошибка");
			project.SetInstallState(CatalogInstallState.Available);
			LocalizationService.Show(this, "Не удалось скачать проект.\n\n" + ex2.Message, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			_activeInstalls--;
			_installingModpack = false;
		}
	}

	private bool BackupPreviousVersion(InstalledContentRecord record, string newPath)
	{
		try
		{
			string value = Path.GetFullPath(_instance.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string text = Path.GetFullPath(Path.Combine(_instance.DirectoryPath, record.RelativePath));
			if (!text.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (text.Equals(newPath, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (!File.Exists(text))
			{
				text += ".disabled";
			}
			if (!File.Exists(text))
			{
				return true;
			}
			string text2 = Path.Combine(_instance.DirectoryPath, ".orbit", "previous-mod-versions");
			Directory.CreateDirectory(text2);
			string destFileName = Path.Combine(text2, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{Path.GetFileName(text)}");
			File.Move(text, destFileName);
			return true;
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (_installingModpack)
		{
			e.Cancel = true;
			MessageBox.Show(this, LocalizationService.IsEnglish ? "Wait for the modpack installation to finish." : "Дождитесь завершения установки модпака.", "Orbit Launcher");
		}
		base.OnClosing(e);
	}

	private void StopBackgroundWork()
	{
		if (_closed)
		{
			return;
		}
		_closed = true;
		_windowCancellation.Cancel();
		_searchCancellation?.Cancel();
		_imageCancellation?.Cancel();
		_detailsCancellation?.Cancel();
		_searchDelayCancellation?.Cancel();
		foreach (AnimatedGifImage item in FindVisualChildren<AnimatedGifImage>(DescriptionHost))
		{
			item.Release();
		}
		DescriptionHost.Children.Clear();
		DetailContent.DataContext = null;
		Results.Clear();
		_imageCache.Clear();
		_searchCancellation?.Dispose();
		_imageCancellation?.Dispose();
		_detailsCancellation?.Dispose();
		_searchDelayCancellation?.Dispose();
		_windowCancellation.Dispose();
	}

	private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
	{
		for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, index);
			if (child is T val)
			{
				yield return val;
			}
			foreach (T item in FindVisualChildren<T>(child))
			{
				yield return item;
			}
		}
	}

	private void ApplyKnownInstallState(ModSearchResult project)
	{
		if (_knownInstallStates.TryGetValue(ProjectStateKey(project), out var value))
		{
			project.SetInstallState(value);
			return;
		}
		InstalledContentRecord? installedContentRecord = _installedContentRecords.FirstOrDefault((InstalledContentRecord record) => string.Equals(record.SourceId, project.SourceId, StringComparison.OrdinalIgnoreCase) && string.Equals(record.ProjectId, project.ProjectId, StringComparison.OrdinalIgnoreCase));
		CatalogInstallState value2;
		if (installedContentRecord != null && _installedContentRegistry.FileStillExists(_instance, installedContentRecord))
		{
			CatalogInstallState catalogInstallState = (string.Equals(installedContentRecord.State, "downloaded", StringComparison.OrdinalIgnoreCase) ? CatalogInstallState.Downloaded : CatalogInstallState.Installed);
			project.SetInstallState(catalogInstallState);
			RememberInstallState(project, catalogInstallState);
		}
		else if (string.Equals(project.SourceId, "modrinth", StringComparison.OrdinalIgnoreCase) && _detectedModrinthProjects.TryGetValue(project.ProjectId, out value2))
		{
			project.SetInstallState(value2);
			RememberInstallState(project, value2);
		}
		else if (IsFabricApiProject(project) && IsFabricApiInstalled())
		{
			project.SetInstallState(CatalogInstallState.Installed);
			RememberInstallState(project, CatalogInstallState.Installed);
		}
	}

	private async Task DetectExistingModrinthContentAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_detectedModrinthProjects.Clear();
		List<LocalCatalogFile> list = new List<LocalCatalogFile>();
		AddLocalCatalogFiles(list, _instance.ModsPath, CatalogInstallState.Installed, ".jar", ".jar.disabled");
		AddLocalCatalogFiles(list, Path.Combine(_instance.DirectoryPath, "resourcepacks"), CatalogInstallState.Installed, ".zip");
		AddLocalCatalogFiles(list, Path.Combine(_instance.DirectoryPath, "shaderpacks"), CatalogInstallState.Installed, ".zip");
		AddLocalCatalogFiles(list, Path.Combine(_instance.DirectoryPath, ".orbit", "downloads", "datapacks"), CatalogInstallState.Downloaded, ".zip");
		AddLocalCatalogFiles(list, Path.Combine(_instance.DirectoryPath, ".orbit", "downloads", "modpacks"), CatalogInstallState.Downloaded, ".mrpack");
		if (list.Count == 0)
		{
			return;
		}
		try
		{
			List<(string Hash, CatalogInstallState State)> hashedFiles = new List<(string, CatalogInstallState)>();
			foreach (LocalCatalogFile localFile in list)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					await using FileStream stream = File.OpenRead(localFile.Path);
					string item = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
					hashedFiles.Add((item, localFile.State));
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
			IReadOnlyDictionary<string, string> readOnlyDictionary = await _modrinth.GetProjectIdsByHashesAsync(hashedFiles.Select<(string, CatalogInstallState), string>(((string Hash, CatalogInstallState State) tuple2) => tuple2.Hash).ToList(), cancellationToken);
			foreach (var (key, catalogInstallState) in hashedFiles)
			{
				if (readOnlyDictionary.TryGetValue(key, out var value) && (!_detectedModrinthProjects.TryGetValue(value, out var value2) || (value2 == CatalogInstallState.Downloaded && catalogInstallState == CatalogInstallState.Installed)))
				{
					_detectedModrinthProjects[value] = catalogInstallState;
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static void AddLocalCatalogFiles(ICollection<LocalCatalogFile> files, string directory, CatalogInstallState state, params string[] extensions)
	{
		if (!Directory.Exists(directory))
		{
			return;
		}
		try
		{
			foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
			{
				if (extensions.Any((string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
				{
					files.Add(new LocalCatalogFile(path, state));
				}
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private void RememberInstallState(ModSearchResult project, CatalogInstallState state)
	{
		_knownInstallStates[ProjectStateKey(project)] = state;
	}

	private static string ProjectStateKey(ModSearchResult project)
	{
		return "project|" + project.SourceId + "|" + project.ProjectId;
	}

	private static bool IsFabricApiProject(ModSearchResult project)
	{
		if (!string.Equals(project.ProjectId, "P7dR8mSH", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(project.Title.Trim(), "Fabric API", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private bool IsFabricApiInstalled()
	{
		if (!string.Equals(_instance.Loader, "Fabric", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(_instance.ModsPath))
		{
			return false;
		}
		try
		{
			return Directory.EnumerateFiles(_instance.ModsPath, "*.jar", SearchOption.TopDirectoryOnly).Select(Path.GetFileNameWithoutExtension).Any((string? name) => name?.Contains("fabric-api", StringComparison.OrdinalIgnoreCase) ?? false);
		}
		catch
		{
			return false;
		}
	}

	private async Task RenderDescriptionAsync(string source, CancellationToken cancellationToken)
	{
		DescriptionHost.Children.Clear();
		string text = NormalizeDescriptionMarkup(source);
		if (string.IsNullOrWhiteSpace(text))
		{
			AddDescriptionText(LocalizationService.Translate("Описание отсутствует."), 0);
			return;
		}
		List<DescriptionImageRow> list = new List<DescriptionImageRow>();
		DescriptionImageRow? descriptionImageRow = null;
		string[] array = text.Split('\n');
		foreach (string text2 in array)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			string text3 = text2.Trim();
			if (text3.Length == 0)
			{
				descriptionImageRow = null;
				AddDescriptionSpacer();
				continue;
			}
			List<DescriptionImageSpec> list2 = ExtractDescriptionImages(text3).ToList();
			string text4 = RemoveDescriptionImages(text3);
			int num = MarkdownHeadingLevel(text4);
			if (num > 0)
			{
				text4 = Regex.Replace(text4, "^#{1,6}\\s*", string.Empty);
			}
			text4 = Regex.Replace(text4, "<[^>]+>", string.Empty);
			text4 = WebUtility.HtmlDecode(text4).Trim().TrimEnd('\\');
			if (text4.Length > 0)
			{
				descriptionImageRow = null;
				AddDescriptionText(text4, num);
			}
			if (list2.Count == 0)
			{
				continue;
			}
			if (text4.Length > 0 || descriptionImageRow == null)
			{
				descriptionImageRow = CreateDescriptionImageRow();
				list.Add(descriptionImageRow);
			}
			foreach (DescriptionImageSpec item in list2)
			{
				AddDescriptionImage(descriptionImageRow, item);
			}
		}
		if (DescriptionHost.Children.Count == 0)
		{
			AddDescriptionText(LocalizationService.Translate("Описание отсутствует."), 0);
		}
		SemaphoreSlim limiter = new SemaphoreSlim(4, 4);
		try
		{
			IEnumerable<Task> tasks = list.SelectMany((DescriptionImageRow row) => ((IEnumerable<DescriptionImageItem>)row.Items).Select((Func<DescriptionImageItem, Task>)async delegate(DescriptionImageItem item)
			{
				await limiter.WaitAsync(cancellationToken);
				try
				{
					await LoadDescriptionImageAsync(row, item, cancellationToken);
				}
				finally
				{
					limiter.Release();
				}
			}));
			try
			{
				await Task.WhenAll(tasks);
			}
			catch (OperationCanceledException)
			{
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

	private DescriptionImageRow CreateDescriptionImageRow()
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		DescriptionHost.Children.Add(stackPanel);
		return new DescriptionImageRow(stackPanel);
	}

	private void AddDescriptionImage(DescriptionImageRow row, DescriptionImageSpec spec)
	{
		AnimatedGifImage animatedGifImage = new AnimatedGifImage
		{
			Stretch = Stretch.Uniform,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top
		};
		Border border = new Border
		{
			Width = 48.0,
			Height = 48.0,
			Background = Brushes.Transparent,
			ClipToBounds = true,
			Child = animatedGifImage
		};
		if (TryNormalizeWebUrl(spec.LinkUrl, out string linkUrl))
		{
			border.Cursor = Cursors.Hand;
			border.ToolTip = linkUrl;
			border.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs eventArgs)
			{
				Process.Start(new ProcessStartInfo(linkUrl)
				{
					UseShellExecute = true
				});
				eventArgs.Handled = true;
			};
		}
		row.Host.Children.Add(border);
		row.Items.Add(new DescriptionImageItem(spec, animatedGifImage, border));
		LayoutDescriptionImageRow(row);
	}

	private void AddDescriptionSpacer()
	{
		if (DescriptionHost.Children.Count != 0)
		{
			UIElementCollection children = DescriptionHost.Children;
			if (children[children.Count - 1] is Border { Tag: string tag } border && tag == "description-spacer")
			{
				border.Height = Math.Min(28.0, border.Height + 4.0);
				return;
			}
			DescriptionHost.Children.Add(new Border
			{
				Tag = "description-spacer",
				Height = 4.0,
				Background = Brushes.Transparent
			});
		}
	}

	private void AddDescriptionText(string text, int headingLevel)
	{
		TextBlock textBlock = new TextBlock();
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.Margin = new Thickness(0.0, (headingLevel > 0) ? 10 : 3, 0.0, 3.0);
		TextBlock textBlock2 = textBlock;
		textBlock2.FontSize = headingLevel switch
		{
			1 => 24, 
			2 => 21, 
			3 => 18, 
			4 => 16, 
			5 => 14, 
			6 => 13, 
			_ => 12, 
		};
		textBlock.FontWeight = ((headingLevel > 0) ? FontWeights.SemiBold : FontWeights.Normal);
		TextBlock textBlock3 = textBlock;
		textBlock3.SetResourceReference(TextBlock.ForegroundProperty, (headingLevel > 0) ? "TextBrush" : "MutedTextBrush");
		AddInlineMarkup(textBlock3, text);
		DescriptionHost.Children.Add(textBlock3);
	}

	private static void AddInlineMarkup(TextBlock block, string text)
	{
		MatchCollection matchCollection = Regex.Matches(text, "(?<bold>\\*\\*(?<boldText>.+?)\\*\\*|__(?<underText>.+?)__)|(?<link>\\[(?<linkText>[^\\]]+)\\]\\((?<linkUrl>https?://[^\\s\\)]+)\\))|(?<code>`(?<codeText>[^`]+)`)|(?<italic>(?<!\\*)\\*(?<italicText>[^*]+)\\*(?!\\*)|(?<!_)_(?<italicUnderText>[^_]+)_(?!_))", RegexOptions.IgnoreCase);
		int num = 0;
		foreach (Match item2 in matchCollection)
		{
			if (item2.Index > num)
			{
				InlineCollection inlines = block.Inlines;
				int num2 = num;
				inlines.Add(new Run(text.Substring(num2, item2.Index - num2)));
			}
			Uri? result;
			if (item2.Groups["bold"].Success)
			{
				string text2 = (item2.Groups["boldText"].Success ? item2.Groups["boldText"].Value : item2.Groups["underText"].Value);
				block.Inlines.Add(new Bold(new Run(text2)));
			}
			else if (item2.Groups["link"].Success && Uri.TryCreate(item2.Groups["linkUrl"].Value, UriKind.Absolute, out result))
			{
				Hyperlink hyperlink = new Hyperlink(new Run(item2.Groups["linkText"].Value))
				{
					NavigateUri = result,
					Foreground = (Brush)Application.Current.FindResource("PrimaryLightBrush")
				};
				hyperlink.SetResourceReference(TextElement.ForegroundProperty, "PrimaryLightBrush");
				hyperlink.RequestNavigate += delegate(object _, RequestNavigateEventArgs args)
				{
					Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri)
					{
						UseShellExecute = true
					});
					args.Handled = true;
				};
				block.Inlines.Add(hyperlink);
			}
			else if (item2.Groups["code"].Success)
			{
				Run item = new Run(item2.Groups["codeText"].Value)
				{
					FontFamily = new FontFamily("Consolas"),
					Foreground = (Brush)Application.Current.FindResource("MutedTextBrush")
				};
				item.SetResourceReference(TextElement.ForegroundProperty, "MutedTextBrush");
				block.Inlines.Add(item);
			}
			else
			{
				string text3 = (item2.Groups["italicText"].Success ? item2.Groups["italicText"].Value : item2.Groups["italicUnderText"].Value);
				block.Inlines.Add(new Italic(new Run(text3)));
			}
			num = item2.Index + item2.Length;
		}
		if (num < text.Length)
		{
			InlineCollection inlines2 = block.Inlines;
			int num2 = num;
			inlines2.Add(new Run(text.Substring(num2, text.Length - num2)));
		}
	}

	private async Task LoadDescriptionImageAsync(DescriptionImageRow row, DescriptionImageItem item, CancellationToken cancellationToken)
	{
		_ = 1;
		try
		{
			byte[]? array = await _imageCache.GetBytesAsync(item.Spec.ImageUrl, 20000000, cancellationToken);
			if (array == null)
			{
				item.Container.Visibility = Visibility.Collapsed;
				LayoutDescriptionImageRow(row);
				return;
			}
			if (!(await item.Image.LoadAsync(array, 760, cancellationToken)))
			{
				item.Container.Visibility = Visibility.Collapsed;
			}
			LayoutDescriptionImageRow(row);
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			item.Container.Visibility = Visibility.Collapsed;
			LayoutDescriptionImageRow(row);
		}
	}

	private void LayoutDescriptionImageRow(DescriptionImageRow row)
	{
		List<DescriptionImageItem> list = row.Items.Where((DescriptionImageItem item) => item.Container.Visibility == Visibility.Visible).ToList();
		if (list.Count == 0)
		{
			row.Host.Visibility = Visibility.Collapsed;
			return;
		}
		row.Host.Visibility = Visibility.Visible;
		List<Size> list2 = list.Select(NaturalDescriptionImageSize).ToList();
		double num = ((DescriptionHost.ActualWidth > 160.0) ? Math.Min(390.0, DescriptionHost.ActualWidth) : 390.0);
		double num2 = list2.Sum((Size size2) => size2.Width) + 8.0 * (double)Math.Max(0, list2.Count - 1);
		double num3 = ((num2 > num) ? (num / num2) : 1.0);
		for (int num4 = 0; num4 < list.Count; num4++)
		{
			DescriptionImageItem descriptionImageItem = list[num4];
			Size size = list2[num4];
			descriptionImageItem.Container.Width = Math.Max(1.0, Math.Round(size.Width * num3, 2));
			descriptionImageItem.Container.Height = Math.Max(1.0, Math.Round(size.Height * num3, 2));
			descriptionImageItem.Container.Margin = new Thickness(0.0, 0.0, (num4 + 1 < list.Count) ? 8.0 : 0.0, 0.0);
		}
	}

	private static Size NaturalDescriptionImageSize(DescriptionImageItem item)
	{
		BitmapSource? bitmapSource = item.Image.Source as BitmapSource;
		int num = Math.Max(1, bitmapSource?.PixelWidth ?? 48);
		int num2 = Math.Max(1, bitmapSource?.PixelHeight ?? 48);
		double? width = item.Spec.Width;
		double num3 = ((width.HasValue && width.GetValueOrDefault() > 0.0) ? width.GetValueOrDefault() : ((double)num));
		width = item.Spec.Height;
		double height = ((width.HasValue && width.GetValueOrDefault() > 0.0) ? width.GetValueOrDefault() : ((double)num2 * num3 / (double)num));
		return new Size(num3, height);
	}

	private static string NormalizeDescriptionMarkup(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return string.Empty;
		}
		return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(source, "<(script|style)\\b[^>]*>.*?</\\1>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline), "\\[\\s*!\\[(?<alt>[^\\]]*)\\]\\(\\s*(?<src><[^>]+>|[^\\s\\)]+)(?:\\s+[\"'][^\"']*[\"'])?\\s*\\)\\s*\\]\\(\\s*(?<href><[^>]+>|[^\\s\\)]+)(?:\\s+[\"'][^\"']*[\"'])?\\s*\\)", (Match match) => OrbitImageToken(match.Groups["src"].Value.Trim('<', '>'), match.Groups["href"].Value.Trim('<', '>'), match.Groups["alt"].Value), RegexOptions.Singleline), "<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']+)[\"'][^>]*>\\s*(?<image><img\\b[^>]*>)\\s*</a>", (Match match) => OrbitImageToken(HtmlAttribute(match.Groups["image"].Value, "src", HtmlAttribute(match.Groups["image"].Value, "data-src")), match.Groups["href"].Value, HtmlAttribute(match.Groups["image"].Value, "alt"), HtmlDimension(match.Groups["image"].Value, "width"), HtmlDimension(match.Groups["image"].Value, "height")), RegexOptions.IgnoreCase | RegexOptions.Singleline), "<h([1-6])\\b[^>]*>(.*?)</h\\1>", (Match match) => $"\n{new string('#', int.Parse(match.Groups[1].Value))} {match.Groups[2].Value}\n", RegexOptions.IgnoreCase | RegexOptions.Singleline), "<(strong|b)\\b[^>]*>(.*?)</\\1>", "**$2**", RegexOptions.IgnoreCase | RegexOptions.Singleline), "<(em|i)\\b[^>]*>(.*?)</\\1>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline), "<a\\b[^>]*href\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]*>(?<label>.*?)</a>", "[${label}](${url})", RegexOptions.IgnoreCase | RegexOptions.Singleline), "<br\\s*/?>|</p>|</div>|</li>|</blockquote>", "\n", RegexOptions.IgnoreCase), "<(p|div|ul|ol|blockquote)\\b[^>]*>", "\n", RegexOptions.IgnoreCase), "<li\\b[^>]*>", "• ", RegexOptions.IgnoreCase).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
	}

	private static int MarkdownHeadingLevel(string line)
	{
		Match match = Regex.Match(line, "^(#{1,6})\\s+");
		if (!match.Success)
		{
			return 0;
		}
		return match.Groups[1].Length;
	}

	private static IEnumerable<DescriptionImageSpec> ExtractDescriptionImages(string line)
	{
		foreach (Match item in Regex.Matches(line, "<orbit-image\\b(?<attributes>[^>]*)/>", RegexOptions.IgnoreCase))
		{
			string value = item.Groups["attributes"].Value;
			if (TryNormalizeWebUrl(HtmlAttribute(value, "src"), out string url))
			{
				yield return new DescriptionImageSpec(url, HtmlAttribute(value, "href"), HtmlAttribute(value, "alt"), HtmlDimension(value, "width"), HtmlDimension(value, "height"));
			}
		}
		foreach (Match item2 in Regex.Matches(line, "!\\[[^\\]]*\\]\\((?<url><[^>]+>|[^\\s\\)]+)(?:\\s+[\"'][^\"']*[\"'])?\\)"))
		{
			if (TryNormalizeWebUrl(item2.Groups["url"].Value.Trim('<', '>'), out string url2))
			{
				yield return new DescriptionImageSpec(url2, string.Empty, string.Empty, null, null);
			}
		}
		foreach (Match item3 in Regex.Matches(line, "<img\\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase))
		{
			string value2 = item3.Groups["attributes"].Value;
			if (TryNormalizeWebUrl(HtmlAttribute(value2, "src", HtmlAttribute(value2, "data-src")), out string url3))
			{
				yield return new DescriptionImageSpec(url3, string.Empty, HtmlAttribute(value2, "alt"), HtmlDimension(value2, "width"), HtmlDimension(value2, "height"));
			}
		}
	}

	private static string RemoveDescriptionImages(string line)
	{
		return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(line, "<orbit-image\\b[^>]*/>", string.Empty, RegexOptions.IgnoreCase), "!\\[[^\\]]*\\]\\((?:<[^>]+>|[^\\)]+)\\)", string.Empty), "\\[\\s*\\]\\([^\\)]+\\)", string.Empty), "<img\\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
	}

	private static string OrbitImageToken(string source, string href, string alt, double? width = null, double? height = null)
	{
		return $"<orbit-image src=\"{WebUtility.HtmlEncode(source)}\" href=\"{WebUtility.HtmlEncode(href)}\" alt=\"{WebUtility.HtmlEncode(alt)}\"" + ((width.HasValue && width.GetValueOrDefault() > 0.0) ? $" width=\"{width.Value}\"" : string.Empty) + ((height.HasValue && height.GetValueOrDefault() > 0.0) ? $" height=\"{height.Value}\"" : string.Empty) + " />";
	}

	private static string HtmlAttribute(string markup, string name, string fallback = "")
	{
		Match match = Regex.Match(markup, "(?:^|\\s)" + Regex.Escape(name) + "\\s*=\\s*(?:[\"'](?<quoted>[^\"']*)[\"']|(?<plain>[^\\s>]+))", RegexOptions.IgnoreCase);
		return WebUtility.HtmlDecode(match.Groups["quoted"].Success ? match.Groups["quoted"].Value : (match.Groups["plain"].Success ? match.Groups["plain"].Value : fallback));
	}

	private static double? HtmlDimension(string markup, string name)
	{
		Match match = Regex.Match(HtmlAttribute(markup, name), "^\\s*(?<number>\\d+(?:\\.\\d+)?)");
		if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !(result > 0.0))
		{
			return null;
		}
		return result;
	}

    private static bool TryNormalizeWebUrl(string value, out string url)
    {
        value = WebUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        { url = uri.AbsoluteUri; return true; }
        url = string.Empty; return false;
    }

	private void OpenProjectWebsite_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedDetails != null)
		{
			string text = _selectedDetails.WebsiteUrl;
			if (string.IsNullOrWhiteSpace(text))
			{
				string text2 = (string.IsNullOrWhiteSpace(_selectedDetails.Slug) ? _selectedDetails.ProjectId : _selectedDetails.Slug);
				text = ((_selectedDetails.SearchResult.SourceId == "curseforge") ? ("https://www.curseforge.com/minecraft/mc-mods/" + text2) : ("https://modrinth.com/project/" + text2));
			}
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true
			});
		}
	}

	private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_lastDownloadPath))
		{
			string? directoryName = Path.GetDirectoryName(_lastDownloadPath);
			if (directoryName != null)
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directoryName + "\"")
				{
					UseShellExecute = true
				});
			}
		}
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		DragMove();
	}

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
