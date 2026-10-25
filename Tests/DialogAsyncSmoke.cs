using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using OrbitLauncher;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;

internal static class DialogAsyncSmoke
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "SoftIcons", "OrbitTheme" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/OrbitLauncher;component/Themes/{theme}.xaml", UriKind.Relative) });
            app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Shutdown(); };
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using var client = new HttpClient(new DialogHandler());
                    var edit = new EditInstanceDialog(client, new LauncherSettings(),
                        new GameInstance { Name = "Fixture", Loader = "Fabric", LoaderVersion = "0.19.3", GameVersion = "1.21.1" });
                    Prepare(edit); edit.Show();
                    var version = (TextBox)edit.FindName("GameVersionBox");
                    var save = (Button)edit.FindName("SaveButton");
                    await Until(() => save.IsEnabled && edit.LoaderVersions.Count > 0);
                    version.Text = "26.2";
                    if (save.IsEnabled) throw new Exception("Save was enabled while the version selection was stale");
                    ((TextBox)edit.FindName("NameBox")).Focus();
                    await Until(() => save.IsEnabled && edit.LoaderVersions.SingleOrDefault()?.Id == "0.19.5");
                    Console.WriteLine("PASS dialog: typing a game version then leaving focus refreshes loaders; save disabled during request");
                    version.Text = "1.20.1"; await Task.Delay(380); version.Text = "26.2";
                    await Until(() => save.IsEnabled && edit.LoaderVersions.SingleOrDefault()?.Id == "0.19.5");
                    await Task.Delay(600);
                    if (edit.LoaderVersions.Single().Id != "0.19.5") throw new Exception("Old loader request overwrote the current selection");
                    Console.WriteLine("PASS dialog: a late previous response cannot replace the selected game's loaders");
                    var orbit = (CheckBox)edit.FindName("OrbitClientCheck");
                    orbit.IsChecked = true; orbit.IsChecked = false;
                    await Until(() => version.IsEnabled && ((ComboBox)edit.FindName("LoaderBox")).IsEnabled);
                    Console.WriteLine("PASS dialog: disabling Orbit Client during a pending request unlocks controls");
                    edit.Close();

                    using var offline = new HttpClient(new DialogHandler { Fail = true });
                    var cached = new EditInstanceDialog(offline, new LauncherSettings(), new GameInstance { Name = "Offline", Loader = "Fabric", LoaderVersion = "0.19.3", GameVersion = "1.21.1" });
                    Prepare(cached); cached.Show();
                    await Until(() => ((Button)cached.FindName("SaveButton")).IsEnabled);
                    if (cached.LoaderVersions.Single().Id != "0.19.3") throw new Exception("Offline edit discarded the installed loader");
                    cached.Close();
                    Console.WriteLine("PASS dialog: offline rename preserves the installed loader version");

                    var create = new CreateInstanceDialog(client, new LauncherSettings());
                    Prepare(create); create.Show();
                    await Until(() => ((CheckBox)create.FindName("OrbitClientCheck")).IsEnabled);
                    var clientCheck = (CheckBox)create.FindName("OrbitClientCheck");
                    clientCheck.IsChecked = true; clientCheck.IsChecked = false;
                    await Until(() => ((ListBox)create.FindName("VersionList")).IsEnabled);
                    create.Close();
                    using var slowClient = new HttpClient(new DialogHandler { SlowManifest = true });
                    var closing = new CreateInstanceDialog(slowClient, new LauncherSettings());
                    Prepare(closing); closing.Show(); closing.Close();
                    await Task.Delay(600);
                    Console.WriteLine("PASS dialog: create/toggle/close during outstanding requests completes without an unhandled error");

                    var earlier = new TaskCompletionSource<IReadOnlyList<CatalogVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var latest = new TaskCompletionSource<IReadOnlyList<CatalogVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var requests = 0;
                    var picker = new VersionPickerWindow(new GameInstance { GameVersion = "26.2", Loader = "Fabric" },
                        new ModProjectDetails { Title = "Race fixture", GameVersions = new[] { "26.2", "1.20.1" }, Loaders = new[] { "Fabric" } },
                        (_, _, _) => ++requests == 1 ? earlier.Task : latest.Task);
                    Prepare(picker); picker.Show(); await Until(() => requests == 1);
                    ((ComboBox)picker.FindName("GameVersionCombo")).SelectedValue = "1.20.1";
                    await Until(() => requests == 2);
                    latest.SetResult(new[] { new CatalogVersion("latest", "Latest", "new.jar", "release", null, new[] { "1.20.1" }, new[] { "Fabric" }) });
                    await Until(() => picker.Versions.SingleOrDefault()?.Id == "latest");
                    earlier.SetResult(new[] { new CatalogVersion("earlier", "Earlier", "old.jar", "release", null, new[] { "26.2" }, new[] { "Fabric" }) });
                    await picker.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    ((ComboBox)picker.FindName("ChannelCombo")).SelectedValue = "release";
                    if (picker.Versions.SingleOrDefault()?.Id != "latest") throw new Exception("Cancelled version response replaced the current channel's backing list");
                    picker.Close();
                    Console.WriteLine("PASS dialog: late cancelled version response cannot overwrite a newer list after channel filtering");
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Dialog asynchronous regression failed", failure);
    }

    private static void Prepare(Window window) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -4000; window.Top = -4000; window.Opacity = 0; window.ShowInTaskbar = false; }
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Dialog state did not settle"); await Task.Delay(25); }
    }
    private sealed class DialogHandler : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public bool SlowManifest { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            // Deliberately ignore transport cancellation to exercise stale-result guards.
            await Task.Delay(path.Contains("1.20.1") || (SlowManifest && path.Contains("manifest")) ? 550 : 80);
            if (Fail) throw new HttpRequestException("offline fixture");
            var json = path.Contains("manifest")
                ? "{\"versions\":[{\"id\":\"26.2\",\"type\":\"release\",\"releaseTime\":\"2026-06-01T00:00:00Z\"},{\"id\":\"1.21.1\",\"type\":\"release\",\"releaseTime\":\"2024-08-01T00:00:00Z\"}]}"
                : "[{\"loader\":{\"version\":\"" + (path.EndsWith("26.2") ? "0.19.5" : "0.19.3") + "\",\"stable\":true}}]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json"), RequestMessage = request };
        }
    }
}
