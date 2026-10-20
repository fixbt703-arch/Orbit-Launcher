using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrbitLauncher;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

// Offline rendered regression: fixtures never launch Minecraft or change user settings.
internal static class LauncherDesignSmoke
{
    public static void Run(string output)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Render(output); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Launcher design regression failed", failure);
    }

    private static void Render(string output)
    {
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "SoftIcons", "OrbitTheme" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/OrbitLauncher;component/Themes/{theme}.xaml", UriKind.Relative) });
        Exception? failure = null;
        app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Shutdown(); };
        using var http = new HttpClient(new OfflineOnlyHandler());
        var main = new MainWindow();
        RemoveLoaded(main);
        Prepare(main);
        main.Loaded += async (_, _) =>
        {
            try
            {
                var names = new[] { "BedWars", "Креатив сборка", "Выживание 26.2", "Красота", "26.3", "26.2", "Бедварс тренировка", "Для картинок", "Тестовая сборка с очень длинным названием — Fabric 26.2" };
                foreach (var name in names)
                    main.Instances.Add(new GameInstance { Name = name, GameVersion = "26.2", Loader = "Fabric", LoaderVersion = "0.19.5", LastPlayedAt = name == "BedWars" ? DateTime.Now : null, CreatedAt = DateTime.Today, DirectoryPath = Path.Combine(output, "fixture-does-not-exist") });
                Invoke(main, "ApplyInstanceFilter");
                ((ComboBox)main.FindName("ModsInstanceCombo")).SelectedIndex = 0;
                for (int i = 0; i < 8; i++) main.InstalledMods.Add(new InstalledMod { Name = new[] { "Sodium", "Orbit Client", "Fabric API", "FerriteCore" }[i % 4], IsEnabled = i < 6, SizeBytes = 1312314, ModifiedAt = DateTime.Today });
                Invoke(main, "UpdateContentEmptyState");
                ((TextBlock)main.FindName("ModsCountText")).Text = "8";
                ((TextBlock)main.FindName("ModsSummaryText")).Text = "BedWars · Моды · 8 файлов";
                for (int i = 0; i < 4; i++) main.SkinLibrary.Add(new SkinLibraryItem { Name = new[] { "Основной", "Adventure", "PvP", "Classic" }[i] });
                ((TextBlock)main.FindName("AccountNameText")).Text = "OrbitPlayer";
                ((TextBlock)main.FindName("AccountModeText")).Text = "Microsoft · Minecraft";
                ((Button)main.FindName("GateLoginButton")).Visibility = Visibility.Collapsed;
                // Preserve real navigation handlers; suppress only network-backed page refresh in this fixture.
                foreach (var navName in new[] { "LibraryNav", "ModsNav", "SkinsNav", "SettingsNav" })
                    ((RadioButton)main.FindName(navName)).Checked -= (RoutedEventHandler)typeof(MainWindow).GetMethod("Navigation_Checked", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(RoutedEventHandler), main);
                Invoke(main, "PopulateSettingsControls");
                typeof(MainWindow).GetMethod("ApplyLauncherBackground", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { "default", false });
                if (!main.IsDefaultBackground || ((FrameworkElement)main.FindName("DefaultSpaceDecoration")).Visibility != Visibility.Visible) throw new Exception("Standard backdrop missing");
                RequireContrast("TextBrush", "CardBrush", 7);
                RequireContrast("MutedTextBrush", "CardBrush", 4.5);
                RequireContrast("TextOnAccentBrush", "PrimaryBrush", 4.5);
                RequireContrast("ControlHoverBorderBrush", "InputBrush", 3);
                RequireContrast("FocusBrush", "InputBrush", 3);
                if (((SolidColorBrush)app.FindResource("ControlBorderBrush")).Color != ((SolidColorBrush)app.FindResource("BorderBrush")).Color)
                    throw new Exception("Idle input borders differ from the shared surface border");
                RequireClientFrame(main);
                foreach (var state in Enum.GetValues<RepairState>())
                {
                    var line = new RepairLine("fixture.jar", "Fixture", state);
                    if (app.TryFindResource($"Icon{char.ConvertToUtf32(line.Glyph, 0):X4}") is not Geometry)
                        throw new Exception($"Missing repair icon: {state}");
                }
                foreach (var size in new[] { (1320d, 820d, ""), (1060d, 680d, "-compact"), (1600d, 900d, "-wide") })
                {
                    main.Width = size.Item1; main.Height = size.Item2;
                    foreach (var page in new[] { "Library", "Mods", "Skins", "Settings" })
                    {
                        Select(main, page);
                        await Settle(main);
                        Capture(main, output, page.ToLowerInvariant() + size.Item3);
                        AssertBounds(main, page == "Mods" ? "ModsInstanceCombo" : page == "Library" ? "RecentInstancePanel" : page + "Page");
                    }
                }
                main.Width = 1320; main.Height = 820; Select(main, "Library");
                ((TextBox)main.FindName("InstanceSearchBox")).Text = "нет такой сборки";
                await Settle(main); Capture(main, output, "library-empty-search");
                if (main.VisibleInstances.Count != 0) throw new Exception("Instance search did not filter fixtures");
                if (((TextBlock)main.FindName("EmptyInstancesTitle")).Text != "Ничего не найдено") throw new Exception("Search showed an empty library message");
                ((Button)main.FindName("ClearInstanceSearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (main.VisibleInstances.Count != names.Length) throw new Exception("Clear search did not restore instances");
                await Settle(main);
                var action = Descendants(main).OfType<Button>().First(button => button.ToolTip?.ToString() == "Действия со сборкой");
                if (action.ActualWidth != 32 || action.ActualHeight != 32) throw new Exception("Instance menu target must be square 32 DIP");
                await CheckButtonStates(main, action, output, "library-menu-hover");
                var play = Descendants(main).OfType<Button>().First(button => ReferenceEquals(button.Style, main.FindResource("CompactQuietButton")));
                await CheckButtonStates(main, play, output, "library-play-hover");
                var launch = Descendants(main).OfType<Button>().First(button => button.ToolTip?.ToString() == "Запустить сборку");
                await CheckButtonStates(main, launch, output, "library-launch-hover");
                var import = Descendants(main).OfType<Button>().First(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "Импорт"));
                await CheckButtonStates(main, import, output, "library-import-hover");
                await CheckMotion(main, launch, output);
                await CheckNavigation(main, output, "default");
                typeof(MainWindow).GetMethod("ApplyLauncherBackground", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { "forest", false });
                if (main.IsDefaultBackground || ((FrameworkElement)main.FindName("DefaultSpaceDecoration")).Visibility != Visibility.Collapsed) throw new Exception("Standard backdrop leaks over custom image");
                if (((Image)main.FindName("LauncherBackgroundImage")).Source is null) throw new Exception("Custom background did not load");
                await Settle(main); Capture(main, output, "library-background", 144);
                await CheckNavigation(main, output, "custom");
                var themedCard = ((SolidColorBrush)main.FindResource("CardBrush")).Color;
                var defaultCard = ((SolidColorBrush)app.FindResource("CardBrush")).Color;
                if (themedCard.R != defaultCard.R || themedCard.G != defaultCard.G || themedCard.B != defaultCard.B) throw new Exception("Custom background lost theme hue");
                typeof(MainWindow).GetMethod("ApplyLauncherBackground", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { "default", false });
                if (!main.IsDefaultBackground || main.Resources.Contains("CardBrush")) throw new Exception("Standard surface did not restore after custom background");
                await Settle(main);
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = Descendants(main).OfType<Border>().First(border => border.Name == "InstanceCard").ContextMenu!;
                if (!menu.IsOpen || menu.DataContext is not GameInstance) throw new Exception("Visible instance menu lost its target");
                menu.IsOpen = false;
                if (!((TextBox)main.FindName("InstanceSearchBox")).Focus()) throw new Exception("Search cannot receive keyboard focus");
                Capture(main, output, "library-keyboard-focus", 144);
                LocalizationService.SetLanguage("en"); Invoke(main, "ApplyInstanceFilter"); LocalizationService.Apply(main);
                main.Width = 1060;
                await Settle(main); Capture(main, output, "library-english");
                if (Descendants(main).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == "Играть")) throw new Exception("Play labels did not localize");
                LocalizationService.SetLanguage("ru"); LocalizationService.Apply(main);
                main.Width = 1320;
                InterfaceTheme.ApplyContrast(true);
                await Settle(main); Capture(main, output, "library-high-contrast");
                InterfaceTheme.ApplyContrast(false);
                var instance = main.Instances[0];
                var create = new CreateInstanceDialog(http, new LauncherSettings());
                RemoveLoaded(create);
                create.VisibleVersions.Add(new MinecraftVersionInfo { Id = "26.2", ReleaseTime = DateTime.Today });
                ((TextBlock)create.FindName("VersionLoadingText")).Text = "1 версия";
                await Dialog(create, "create-instance");
                await Dialog(new EditInstanceDialog(http, new LauncherSettings(), instance), "edit-instance");
                await Dialog(new NewSkinDialog(), "new-skin");
                await Dialog(new TextPromptDialog("Переименовать сборку", "Название сборки", "BedWars"), "rename-instance");
                var catalog = new ModBrowserWindow(http, instance, new ObservableCollection<DownloadItem>(), "https://offline.test", curseForgeApiBase: "https://offline.test", curseForgeApiKey: "preview");
                RemoveLoaded(catalog);
                typeof(ModBrowserWindow).GetField("_suppressFilterSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(catalog, true);
                catalog.GameVersions.Add(new ModSearchOption("26.2", "26.2"));
                catalog.Loaders.Add(new ModSearchOption("fabric", "Fabric"));
                catalog.Categories.Add(new ModCategoryOption("", "Все категории"));
                foreach (var combo in new[] { "CatalogSourceCombo", "ProjectTypeCombo", "GameVersionCombo", "LoaderCombo", "CategoryCombo", "EnvironmentCombo", "SortCombo" }) ((ComboBox)catalog.FindName(combo)).SelectedIndex = 0;
                foreach (var name in new[] { "Sodium", "FerriteCore", "Fabric API", "ImmediatelyFast" })
                    catalog.Results.Add(new ModSearchResult { Title = name, Author = "Developer", Description = "Оптимизация Minecraft для плавной игры и стабильной частоты кадров.", Downloads = 68400000, Followers = 12000, DateModified = DateTime.Today, Categories = new[] { "optimization" }, Loaders = new[] { "fabric", "forge", "neoforge" }, Environment = "client" });
                Prepare(catalog); catalog.Show();
                RequireClientFrame(catalog);
                ((TextBlock)catalog.FindName("StatusText")).Text = "Modrinth: показано 4 из 4";
                await Settle(catalog); Capture(catalog, output, "catalog");
                var sourceSelector = (ComboBox)catalog.FindName("CatalogSourceCombo");
                foreach (var providerIndex in new[] { 0, 1 })
                {
                    sourceSelector.SelectedIndex = providerIndex;
                    sourceSelector.IsDropDownOpen = true;
                    await Settle(catalog);
                    var popup = (Popup)sourceSelector.Template.FindName("PART_Popup", sourceSelector);
                    var providerMenu = (FrameworkElement)popup.Child;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(providerMenu.ActualWidth * 2), (int)Math.Ceiling(providerMenu.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                    bitmap.Render(providerMenu);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(output, $"catalog-provider-menu-{providerIndex}.png"))) encoder.Save(file);
                    sourceSelector.IsDropDownOpen = false;
                    await Settle(catalog); Capture(catalog, output, $"catalog-provider-{providerIndex}", 144);
                }
                sourceSelector.SelectedIndex = 0;
                ((FrameworkElement)catalog.FindName("ResultsPane")).Visibility = Visibility.Collapsed;
                ((FrameworkElement)catalog.FindName("DetailPanel")).Visibility = Visibility.Visible;
                var detail = (ScrollViewer)catalog.FindName("DetailContent");
                detail.Visibility = Visibility.Visible;
                detail.DataContext = new ModProjectDetails { Title = "Sodium", Author = "Developer", Summary = "Оптимизация рендеринга Minecraft для плавной игры.", GameVersions = new[] { "26.2" }, Loaders = new[] { "Fabric" }, SearchResult = catalog.Results[0], Downloads = 68400000, UpdatedAt = DateTime.Today, License = "LGPL-3.0" };
                catalog.Width = 980; catalog.Height = 640;
                await Settle(catalog); Capture(catalog, output, "catalog-details-compact");
                var back = (Button)catalog.FindName("BackToCatalogButton");
                if (back.ActualHeight != 32 || back.ActualWidth > 150) throw new Exception("Catalog back button stretched into header");
                await CheckButtonStates(catalog, back, output, "catalog-back-hover");
                ((Button)catalog.FindName("BackToCatalogButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (((FrameworkElement)catalog.FindName("ResultsPane")).Visibility != Visibility.Visible || detail.DataContext is not null) throw new Exception("Catalog back action failed");
                catalog.Close();
                var repair = new RepairModsWindow(instance, http, "https://offline.test", "https://offline.test", "preview");
                RemoveLoaded(repair);
                typeof(RepairModsWindow).GetField("_running", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(repair, false);
                repair.Updates.Add(new RepairLine("fabric-api.jar", "Совместимая версия установлена", RepairState.Installed));
                repair.Updates.Add(new RepairLine("example.jar", "Нужна проверка зависимостей", RepairState.NeedsAttention));
                await Dialog(repair, "repair-mods");
                var project = new ModProjectDetails { Title = "Sodium", GameVersions = new[] { "26.2" }, Loaders = new[] { "Fabric" } };
                var versions = new VersionPickerWindow(instance, project, (_, _, _) => Task.FromResult<IReadOnlyList<CatalogVersion>>(Array.Empty<CatalogVersion>()));
                versions.Versions.Add(new CatalogVersion("preview", "Sodium 26.2", "sodium-26.2.jar", "release", DateTime.Today, new[] { "26.2" }, new[] { "Fabric" }));
                await Dialog(versions, "versions");
                var gallery = new GalleryViewerWindow(new[] { new ModGalleryImage { Title = "Minecraft", Url = "https://offline.test/preview" } }, 0, new ImageCacheService(http)) { WindowState = WindowState.Normal, Width = 1060, Height = 680 };
                ((Image)gallery.FindName("LargeImage")).Source = new BitmapImage(new Uri("pack://application:,,,/OrbitLauncher;component/Assets/forest.png"));
                await Dialog(gallery, "gallery");
                Console.WriteLine("PASS design: four pages at 1060/1320/1600, search, instance menu, English, focus, contrast ratios, eight dialogs, catalog back action, hover/pressed/focus/disabled geometry, centered icons, nine instances, default/custom/default background switch; offline fixtures");

                async Task Dialog(Window window, string name)
                {
                    RemoveLoaded(window); Prepare(window); window.Show();
                    RequireClientFrame(window);
                    await Settle(window); Capture(window, output, name); window.Close();
                }
            }
            catch (Exception e) { failure = e; }
            finally { main.Close(); app.Shutdown(); }
        };
        main.Show(); app.Run();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Prepare(Window window) { window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -5000; window.Top = -5000; }
    private static void RemoveLoaded(Window window)
    {
        var store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
        if (store is null) return;
        var handlers = (RoutedEventHandlerInfo[]?)store.GetType().GetMethod("GetRoutedEventHandlers")!.Invoke(store, new object[] { FrameworkElement.LoadedEvent });
        foreach (var info in handlers ?? Array.Empty<RoutedEventHandlerInfo>()) window.RemoveHandler(FrameworkElement.LoadedEvent, info.Handler);
    }
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static void Select(MainWindow main, string selected)
    {
        foreach (var name in new[] { "Library", "Mods", "Skins", "Settings" })
        {
            ((Grid)main.FindName(name + "Page")).Visibility = name == selected ? Visibility.Visible : Visibility.Collapsed;
            ((RadioButton)main.FindName(name + "Nav")).IsChecked = name == selected;
        }
        ((FrameworkElement)main.FindName("SkinsSignInPanel")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)main.FindName("SkinsContentPanel")).Visibility = Visibility.Visible;
    }
    private static async Task Settle(Window window) { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(260); window.UpdateLayout(); }
    private static void Capture(Window window, string output, string name, double dpi = 96)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi / 96), (int)Math.Ceiling(visual.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    // Inject WPF dependency-property states into the real templates without moving the desktop pointer.
    private static async Task CheckButtonStates(Window window, Button button, string output, string name)
    {
        var baseline = button.RenderSize;
        var border = (Border)button.Template.FindName("Border", button);
        void CheckGeometry()
        {
            if (button.RenderSize != baseline) throw new Exception($"Button size changed: {name}");
            var bounds = border.TransformToAncestor(button).TransformBounds(new Rect(border.RenderSize));
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > baseline.Width + .5 || bounds.Bottom > baseline.Height + .5)
                throw new Exception($"Button state escaped its bounds: {name}: {bounds}");
            var presenter = Descendants(border).OfType<ContentPresenter>().First();
            var content = presenter.TransformToAncestor(button).TransformBounds(new Rect(presenter.RenderSize));
            if (Math.Abs(content.Left + content.Width / 2 - baseline.Width / 2) > 1 || Math.Abs(content.Top + content.Height / 2 - baseline.Height / 2) > 1)
                throw new Exception($"Button content is off center: {name}: {content}");
        }
        CheckGeometry();
        SetState(button, typeof(UIElement), "IsMouseOver", true);
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        await Settle(window); CheckGeometry();
        if (!button.IsMouseOver) throw new Exception("Hover state was not applied");
        var expected = (SolidColorBrush)button.FindResource(ReferenceEquals(button.Style, window.FindResource("PrimaryButton")) ? "PrimaryHoverBrush" : "CardHoverBrush");
        if (((SolidColorBrush)border.Background).Color != expected.Color) throw new Exception($"Hover background did not activate: {name}");
        Capture(window, output, name, 144);
        if (name == "catalog-back-hover") Capture(window, output, name + "-200", 192);
        SetState(button, typeof(ButtonBase), "IsPressed", true);
        await Settle(window); CheckGeometry();
        if (!button.IsPressed || border.Opacity >= 1) throw new Exception($"Pressed state did not activate: {name}, pressed={button.IsPressed}, opacity={border.Opacity}, target={InterfaceMotion.GetTargetOpacity(border)}");
        SetState(button, typeof(ButtonBase), "IsPressed", false);
        SetState(button, typeof(UIElement), "IsMouseOver", false);
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        if (!button.Focus() || !button.IsKeyboardFocused) throw new Exception($"Button cannot receive focus: {name}");
        await Settle(window); CheckGeometry();
        button.IsEnabled = false;
        await Settle(window); CheckGeometry();
        if (border.Opacity >= 1) throw new Exception($"Disabled state did not activate: {name}");
        button.IsEnabled = true;
        await Settle(window);
        Console.WriteLine($"PASS button states {name}: {baseline.Width:F0} x {baseline.Height:F0} DIP; centered, stable bounds");
    }
    private static void SetState(DependencyObject target, Type owner, string property, bool value)
    {
        var key = (DependencyPropertyKey)owner.GetField(property + "PropertyKey", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        // IsMouseOver's CLR getter uses a cached flag; mirror WPF's input state before updating its trigger property.
        if (property == "IsMouseOver")
        {
            var write = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
            write.Invoke(target, new[] { Enum.Parse(write.GetParameters()[0].ParameterType, "IsMouseOverCache"), (object)value });
        }
        target.SetValue(key, value);
    }

    private static async Task CheckNavigation(MainWindow main, string output, string background)
    {
        var buttons = new[] { "LibraryNav", "ModsNav", "SkinsNav", "SettingsNav" }
            .Select(name => (RadioButton)main.FindName(name)).ToArray();
        foreach (var button in buttons) button.IsChecked = false;
        await Settle(main);
        foreach (var button in buttons)
        {
            var surface = (Border)button.Template.FindName("Surface", button);
            SetState(button, typeof(UIElement), "IsMouseOver", true);
            // Inspect a sequence of early/intermediate frames: a final screenshot hid the white flash.
            for (int frame = 0; frame < 5; frame++)
            {
                await Task.Delay(22); main.UpdateLayout();
                var color = ((SolidColorBrush)surface.Background).Color;
                if (color.A > 0 && (color.R > 90 || color.G > 90 || color.B > 100))
                    throw new Exception($"Bright navigation flash on {background}: {button.Name}, frame {frame}, {color}");
                if (button.Name == "ModsNav" && frame == 1) Capture(main, output, $"navigation-hover-{background}-mid", 144);
            }
            SetState(button, typeof(UIElement), "IsMouseOver", false);
            await Task.Delay(35);
            var leaving = ((SolidColorBrush)surface.Background).Color;
            if (leaving.A > 0 && (leaving.R > 90 || leaving.G > 90 || leaving.B > 100))
                throw new Exception($"Bright navigation exit flash: {leaving}");
            SetState(button, typeof(UIElement), "IsMouseOver", true);
            SetState(button, typeof(UIElement), "IsMouseOver", false);
            await Settle(main);
            if (((SolidColorBrush)surface.Background).Color.A != 0) throw new Exception("Navigation did not return to transparency");
        }
        buttons[0].IsChecked = true;
        await Settle(main);
        Console.WriteLine($"PASS navigation {background}: four tabs, early/mid/exit/reversed frames, no white flash");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    private static void RequireClientFrame(Window window)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is null || chrome.GlassFrameThickness != new Thickness(0))
            throw new Exception($"Missing client chrome: {window.Title}");
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds)) throw new Exception("Cannot inspect native window geometry");
        var origin = window.PointToScreen(new Point());
        double topGap = origin.Y - bounds.Top;
        if (Math.Abs(topGap) > 1) throw new Exception($"Native top frame remains on {window.Title}: {topGap} px");
        Console.WriteLine($"PASS client frame {window.Title}: native top gap {topGap:F0} px");
    }

    private static async Task CheckMotion(Window main, Button launch, string output)
    {
        var border = (Border)launch.Template.FindName("Border", launch);
        var idle = ((SolidColorBrush)border.Background).Color;
        var hover = ((SolidColorBrush)launch.FindResource("PrimaryHoverBrush")).Color;
        SetState(launch, typeof(UIElement), "IsMouseOver", true);
        await Task.Delay(50); main.UpdateLayout();
        var halfway = ((SolidColorBrush)border.Background).Color;
        if (InterfaceMotion.CanAnimate && (halfway == idle || halfway == hover))
            throw new Exception($"Hover did not interpolate: {idle} -> {halfway} -> {hover}");
        Capture(main, output, "motion-hover-mid", 144);
        SetState(launch, typeof(UIElement), "IsMouseOver", false);
        SetState(launch, typeof(UIElement), "IsMouseOver", true);
        SetState(launch, typeof(UIElement), "IsMouseOver", false);
        await Settle(main);
        if (((SolidColorBrush)border.Background).Color != idle || launch.Opacity != 1 || border.Opacity != 1)
            throw new Exception("Rapid hover reversal left a stale color or opacity");
        if (((SolidColorBrush)launch.FindResource("PrimaryBrush")).Color != idle)
            throw new Exception("Hover mutated the shared theme brush");

        var page = (UIElement)main.FindName("LibraryPage");
        var original = page.RenderTransform;
        InterfaceMotion.Reveal(page);
        await Task.Delay(50); main.UpdateLayout();
        if (InterfaceMotion.CanAnimate && (page.Opacity >= 1 || page.RenderTransform.Value.OffsetY <= 0))
            throw new Exception("Page entrance did not interpolate");
        Capture(main, output, "motion-page-mid");
        InterfaceMotion.Reveal(page);
        await Settle(main);
        if (page.Opacity != 1 || !ReferenceEquals(page.RenderTransform, original))
            throw new Exception("Interrupted reveal did not restore its original transform");
        InterfaceMotion.Reveal(page);
        InterfaceMotion.SetEnabled(main, false);
        SetState(launch, typeof(UIElement), "IsMouseOver", true);
        if (((SolidColorBrush)border.Background).Color != hover || page.Opacity != 1 || !ReferenceEquals(page.RenderTransform, original))
            throw new Exception("Reduced motion left an animated frame");
        Capture(main, output, "motion-disabled");
        SetState(launch, typeof(UIElement), "IsMouseOver", false);
        InterfaceMotion.SetEnabled(main, true);

        var scroll = new ScrollViewer { Height = 180, CanContentScroll = false, Content = new Border { Height = 1800, Background = Brushes.SlateBlue } };
        var toggle = new CheckBox { Style = (Style)main.FindResource("SwitchToggle"), Content = "Переключатель", Margin = new Thickness(12), Width = 210 };
        var stack = new StackPanel(); stack.Children.Add(toggle); stack.Children.Add(scroll);
        var fixture = new Window { Width = 260, Height = 300, Content = stack, ShowInTaskbar = false };
        Prepare(fixture); fixture.Show(); await Settle(fixture);
        SmoothScroll.SetEnabled(scroll, true);
        SmoothScroll.ScrollBy(scroll, 240);
        await Task.Delay(50); fixture.UpdateLayout();
        if (InterfaceMotion.CanAnimate && (scroll.VerticalOffset <= 0 || scroll.VerticalOffset >= 240))
            throw new Exception($"Scroll did not interpolate: {scroll.VerticalOffset}");
        SmoothScroll.ScrollBy(scroll, 240); SmoothScroll.ScrollBy(scroll, -100);
        await Settle(fixture);
        if (Math.Abs(scroll.VerticalOffset - 380) > 1) throw new Exception($"Scroll lost wheel accumulation: {scroll.VerticalOffset}");
        SmoothScroll.ScrollBy(scroll, 400); await Task.Delay(40); fixture.UpdateLayout();
        SmoothScroll.Cancel(scroll); fixture.UpdateLayout(); double stopped = scroll.VerticalOffset;
        await Settle(fixture);
        if (Math.Abs(scroll.VerticalOffset - stopped) > 1) throw new Exception("Cancelled scroll kept moving");
        SmoothScroll.ScrollBy(scroll, -10000); await Settle(fixture);
        if (scroll.VerticalOffset != 0 || SmoothScroll.ScrollBy(scroll, -50)) throw new Exception("Scroll did not hand off at its boundary");
        InterfaceMotion.SetEnabled(fixture, false); SmoothScroll.ScrollBy(scroll, 200); fixture.UpdateLayout();
        if (Math.Abs(scroll.VerticalOffset - 200) > 1) throw new Exception("Reduced-motion scroll did not move directly");
        InterfaceMotion.SetEnabled(fixture, true);
        toggle.IsChecked = true;
        await Task.Delay(50);
        var thumb = (FrameworkElement)toggle.Template.FindName("Thumb", toggle);
        if (InterfaceMotion.CanAnimate && (thumb.RenderTransform.Value.OffsetX <= 0 || thumb.RenderTransform.Value.OffsetX >= 20))
            throw new Exception("Switch thumb did not interpolate");
        toggle.IsChecked = false; toggle.IsChecked = true;
        await Settle(fixture);
        if (thumb.RenderTransform.Value.OffsetX != 20 || thumb.HorizontalAlignment != HorizontalAlignment.Left)
            throw new Exception("Switch moved its layout or ended at the wrong position");
        var outerContent = new StackPanel();
        fixture.Content = null; outerContent.Children.Add(stack); outerContent.Children.Add(new Border { Height = 1000 });
        var outer = new ScrollViewer { Content = outerContent, CanContentScroll = false };
        SmoothScroll.SetEnabled(outer, true); fixture.Content = outer; await Settle(fixture);
        scroll.ScrollToVerticalOffset(0); outer.ScrollToVerticalOffset(0); fixture.UpdateLayout();
        var source = (UIElement)scroll.Content;
        void Wheel() => source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        if (SystemParameters.WheelScrollLines != 0 && Keyboard.Modifiers == ModifierKeys.None)
        {
            Wheel(); await Settle(fixture);
            if (scroll.VerticalOffset <= 0 || outer.VerticalOffset != 0) throw new Exception("Outer wheel stole nested scrolling");
            scroll.ScrollToBottom(); fixture.UpdateLayout();
            Wheel(); await Settle(fixture);
            if (outer.VerticalOffset <= 0) throw new Exception("Nested scroll did not hand off at its bottom");
        }
        outer.ScrollToVerticalOffset(0); fixture.UpdateLayout();
        SmoothScroll.ScrollBy(outer, 400); await Task.Delay(35); fixture.UpdateLayout();
        outer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fixture), 0, Key.Home) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        fixture.UpdateLayout(); double afterKey = outer.VerticalOffset;
        await Settle(fixture);
        if (Math.Abs(outer.VerticalOffset - afterKey) > 1) throw new Exception("Keyboard input did not interrupt wheel motion");
        SmoothScroll.ScrollBy(outer, 500); outerContent.Children.RemoveAt(1);
        await Settle(fixture);
        if (outer.VerticalOffset > outer.ScrollableHeight + .1) throw new Exception("Extent shrink left an invalid offset");
        scroll.CanContentScroll = true;
        if (SmoothScroll.ScrollBy(scroll, 100)) throw new Exception("Physical scroll intercepted a logical list");
        fixture.Close();
        Console.WriteLine($"PASS motion (Windows animations={InterfaceMotion.CanAnimate}): intermediate hover/page/switch frames; rapid reversals; reduced motion; scroll accumulation/cancel/bounds/nested routing/keyboard/extent/logical-list bypass");
    }

    private static void AssertBounds(Window window, string name)
    {
        var element = (FrameworkElement)window.FindName(name);
        var bounds = element.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(element.RenderSize));
        if (bounds.Left < -1 || bounds.Right > window.ActualWidth + 1 || element.ActualWidth < 1) throw new Exception($"Clipped {name}: {bounds}");
    }
    private static void RequireContrast(string foreground, string background, double minimum)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) { double channel = value / 255d; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(((SolidColorBrush)Application.Current.FindResource(foreground)).Color);
        double b = Luminance(((SolidColorBrush)Application.Current.FindResource(background)).Color);
        double ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        if (ratio < minimum) throw new Exception($"Insufficient contrast: {foreground}/{background} {ratio:F2}");
        Console.WriteLine($"PASS contrast {foreground}/{background}: {ratio:F2}:1");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class OfflineOnlyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }
}
