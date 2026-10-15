using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OrbitLauncher;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using SkiaSharp;

internal static class CatalogPaginationSmoke
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunWindow(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Catalog pagination regression failed", failure);
    }

    private static void RunWindow()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "SoftIcons", "OrbitTheme" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/OrbitLauncher;component/Themes/{theme}.xaml", UriKind.Relative) });
        Exception? failure = null;
        app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Shutdown(); };
        using var client = new HttpClient(new CatalogHandler());
        var instance = new GameInstance { DirectoryPath = Path.Combine(Path.GetTempPath(), "OrbitPagination-" + Guid.NewGuid().ToString("N")), Name = "Pagination test", Loader = "Fabric", GameVersion = "26.2" };
        Directory.CreateDirectory(instance.DirectoryPath);
        var window = new ModBrowserWindow(client, instance, new ObservableCollection<DownloadItem>(), "https://modrinth.test", curseForgeApiBase: "https://curseforge.test", curseForgeApiKey: "test-key")
        { ShowInTaskbar = false, Opacity = 0, Left = -4000, Top = -4000 };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var scroll = (ScrollViewer)window.FindName("ResultsScroll");
                var items = (ItemsControl)window.FindName("ResultsItems");
                var more = (Button)window.FindName("LoadMoreButton");
                foreach (var source in new[] { "modrinth", "curseforge" })
                {
                    if (source == "curseforge") ((ComboBox)window.FindName("CatalogSourceCombo")).SelectedIndex = 1;
                    await Until(() => window.Results.Count == 30 && window.Results[0].SourceId == source && more.IsEnabled);
                    for (int page = 1; page <= 3; page++)
                    {
                        foreach (var fraction in new[] { 0d, .25, .5, .75, 1 })
                        {
                            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * fraction);
                            window.UpdateLayout();
                            await Task.Delay(80);
                            var visible = new List<ModSearchResult>();
                            for (int i = 0; i < items.Items.Count; i++)
                            {
                                var card = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(i);
                                var bounds = card.TransformToAncestor(scroll).TransformBounds(new Rect(card.RenderSize));
                                if (bounds.Bottom > 0 && bounds.Top < scroll.ViewportHeight) visible.Add(window.Results[i]);
                            }
                            if (visible.Count == 0) throw new Exception("No visible cards found");
                            await Until(() => visible.All(project => project.Icon is not null));
                        }
                        if (page < 3)
                        {
                            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (more.IsEnabled) throw new Exception("Load more was not disabled during the request");
                            await Until(() => window.Results.Count == (page + 1) * 30 && more.IsEnabled);
                        }
                    }
                    window.Height = 640;
                    window.Width = 980;
                    window.UpdateLayout();
                    scroll.ScrollToEnd();
                    await Task.Delay(150);
                    await Until(() => window.Results.TakeLast(3).All(project => project.Icon is not null));
                    if (window.Results.Select(project => project.ProjectId).Distinct().Count() != 90) throw new Exception("Pagination duplicated results");
                    Console.WriteLine($"PASS {source}: visible icons on three pages, scroll to end, resize and unique results");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { window.Close(); app.Shutdown(); }
        };
        window.Show();
        app.Run();
        Directory.Delete(instance.DirectoryPath, true);
        if (failure is not null) throw failure;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Catalog or visible icons did not load");
            await Task.Delay(25);
        }
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        private readonly byte[] _icon;
        public CatalogHandler()
        {
            using var bitmap = new SKBitmap(16, 16);
            bitmap.Erase(SKColors.MediumPurple);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            _icon = png.ToArray();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(30, token);
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.StartsWith("/icons/")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(_icon) };
            bool curse = uri.Host == "curseforge.test";
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            int.TryParse(query[curse ? "index" : "offset"], out var offset);
            string json;
            if (uri.AbsolutePath.EndsWith("/search"))
            {
                var hits = Enumerable.Range(offset, 30).Select(i => curse
                    ? (object)new { id = i + 1, name = "Mod " + i, summary = "Description", authors = new[] { new { name = "Developer" } }, logo = new { url = $"https://curseforge.test/icons/{i}.png" } }
                    : new { project_id = i.ToString(), title = "Mod " + i, description = "Description", author = "Developer", icon_url = $"https://modrinth.test/icons/{i}.png" });
                json = curse ? JsonSerializer.Serialize(new { data = hits, pagination = new { totalCount = 90 } }) : JsonSerializer.Serialize(new { hits, total_hits = 90 });
            }
            else json = curse ? "{\"data\":[]}" : "[]";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
