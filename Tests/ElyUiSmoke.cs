using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitLauncher;
using OrbitLauncher.Controls;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

internal static class ElyUiSmoke
{
    public static void Run(string output, bool browserProbe = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "SoftIcons", "OrbitTheme" }) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/OrbitLauncher;component/Themes/{theme}.xaml", UriKind.Relative) });
            app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Shutdown(); };
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    Directory.CreateDirectory(output);
                    LocalizationService.SetLanguage("ru");
                    using var http = new HttpClient(new Handler());
                    var accounts = new ElyAccountService(http, Path.Combine(output, "fixture-session.dat"));
                    var skins = new ElySkinService(http, Path.Combine(output, "fixture-heads"));
                    var main = new MainWindow(); RemoveLoaded(main); Prepare(main);
                    var settings = new LauncherSettings { OfflineModeEnabled = true, OfflinePlayerName = "OrbitTest" };
                    Set(main, "_settings", settings); Set(main, "_elyAccounts", accounts); Set(main, "_elySkins", skins);
                    Set(main, "_settingsService", new SettingsService(Path.Combine(output, "fixture-settings.json")));
                    var panel = (ElySkinsPanel)main.FindName("ElySkins"); panel.Initialize(accounts, skins, Path.Combine(output, "web-profile"));
                    main.Show();
                    ((RadioButton)main.FindName("SkinsNav")).IsChecked = true;
                    await InvokeAsync(main, "UpdateAccountCardAsync"); await Settle(main);
                    foreach (var width in new[] { 1320, 1060 })
                    {
                        main.Width = width; main.Height = width == 1320 ? 820 : 680; await Settle(main);
                        if (!panel.IsVisible || ((Grid)main.FindName("SkinsContentPanel")).IsVisible || ((Border)main.FindName("SkinsSignInPanel")).IsVisible) throw new Exception("Provider panels overlap");
                        Capture(main, output, "ely-disconnected-" + width);
                    }
                    if (!((Button)panel.FindName("ConnectButton")).Focus()) throw new Exception("Connect not keyboard focusable");
                    await accounts.LoginAsync("OrbitTest", "fixture-password");
                    await skins.RefreshHeadAsync("OrbitTest"); await InvokeAsync(main, "UpdateAccountCardAsync"); await Settle(main);
                    if (((TextBlock)main.FindName("AccountNameText")).Text != "OrbitTest" || !((Image)main.FindName("AccountFaceImage")).IsVisible) throw new Exception("Ely account/head not shown");
                    Capture(main, output, "ely-connected-1060");
                    settings.OfflineModeEnabled = false; await InvokeAsync(main, "UpdateAccountCardAsync"); await Settle(main);
                    if (panel.IsVisible || !((Border)main.FindName("SkinsSignInPanel")).IsVisible || ((Image)main.FindName("AccountFaceImage")).IsVisible) throw new Exception("Ely skin leaked into Microsoft provider");
                    settings.OfflineModeEnabled = true; await InvokeAsync(main, "UpdateAccountCardAsync");
                    var auth = typeof(MainWindow).GetField("_authService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                    Set(auth, "_microsoftSession", new CmlLib.Core.Auth.MSession { Username = "OrbitLicensed", UUID = "11111111222243338444555555555555", AccessToken = "fixture-access", UserType = "msa" });
                    auth.GetType().GetProperty("DisplayName")!.SetValue(auth, "OrbitLicensed");
                    Set(main, "_playerSkinService", new PlayerSkinService(http, Path.Combine(output, "microsoft-heads")));
                    Set(main, "_skinLibraryLoaded", true);
                    settings.OfflineModeEnabled = false;
                    await InvokeAsync(main, "UpdateAccountCardAsync"); await Settle(main);
                    var licensedToggle = (CheckBox)main.FindName("LicensedElySkinsCheck");
                    if (!licensedToggle.IsVisible || licensedToggle.IsChecked != true || !licensedToggle.Focus()) throw new Exception("Licensed Ely switch is unavailable");
                    Capture(main, output, "ely-licensed-switch-1060");
                    settings.ShowElySkinsForLicensedAccounts = false;
                    typeof(MainWindow).GetMethod("UpdateSkinAccountState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                    if (licensedToggle.IsChecked != false) throw new Exception("Licensed Ely setting did not synchronize");
                    settings.ShowElySkinsForLicensedAccounts = true;
                    settings.OfflineModeEnabled = true; await InvokeAsync(main, "UpdateAccountCardAsync");
                    ((RadioButton)main.FindName("SettingsNav")).IsChecked = true; await Settle(main); Capture(main, output, "ely-settings-1060");
                    ((RadioButton)main.FindName("SkinsNav")).IsChecked = true;
                    LocalizationService.SetLanguage("en"); LocalizationService.Apply(main); panel.UpdateState(); await Settle(main); Capture(main, output, "ely-english-1060");
                    var login = new ElyLoginWindow(accounts); Prepare(login); login.Show(); await Settle(login); Capture(login, output, "ely-login-english");
                    if (!((TextBox)login.FindName("LoginBox")).Focus() || !((PasswordBox)login.FindName("PasswordInput")).Focus()) throw new Exception("Login keyboard navigation broken");
                    login.Close(); LocalizationService.SetLanguage("ru");
                    var twoFactorAccounts = new ElyAccountService(new HttpClient(new ChallengeHandler()), Path.Combine(output, "2fa-session.dat"));
                    var twoFactor = new ElyLoginWindow(twoFactorAccounts); Prepare(twoFactor); twoFactor.Show();
                    ((TextBox)twoFactor.FindName("LoginBox")).Text = "OrbitTest"; ((PasswordBox)twoFactor.FindName("PasswordInput")).Password = "fixture";
                    ((Button)twoFactor.FindName("LoginButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(twoFactor);
                    if (!((StackPanel)twoFactor.FindName("CodePanel")).IsVisible || !((Button)twoFactor.FindName("LoginButton")).IsEnabled) throw new Exception("2FA recovery unusable");
                    Capture(twoFactor, output, "ely-two-factor"); twoFactor.Close();
                    if (browserProbe)
                    {
                        LocalizationService.Apply(main); panel.UpdateState();
                        await (Task)typeof(ElySkinsPanel).GetMethod("OpenPortalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, new object[] { "https://ely.by/skins/add" })!;
                        var browser = (Microsoft.Web.WebView2.Wpf.WebView2)panel.FindName("Browser");
                        if (browser.CoreWebView2 is null) throw new Exception("WebView2 could not initialize");
                        bool ready = false;
                        for (int attempt = 0; attempt < 50; attempt++)
                        {
                            if (await browser.ExecuteScriptAsync("!!document.querySelector('#skinSelector')") == "true") { ready = true; break; }
                            await Task.Delay(400);
                        }
                        if (!ready) throw new Exception("Official Ely.by upload input not available");
                        await Task.Delay(4000);
                        main.UpdateLayout();
                        using var screenshot = File.Create(Path.Combine(output, "ely-official-upload.png"));
                        await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                        Console.WriteLine("PASS live embedded Ely.by upload page: WebView2 loads, original file input found. No file uploaded and no account credentials entered.");
                        const string otherId = "11223344556677889900aabbccddeeff";
                        typeof(ElyAccountService).GetProperty(nameof(ElyAccountService.Account))!.SetValue(accounts, new ElyAccount("OrbitOther", otherId, "fixture-other", "fixture-client"));
                        await (Task)typeof(ElySkinsPanel).GetMethod("OpenPortalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, new object[] { "https://ely.by/skins/add" })!;
                        var otherBrowser = (Microsoft.Web.WebView2.Wpf.WebView2)panel.FindName("Browser");
                        if (ReferenceEquals(browser, otherBrowser) || otherBrowser.CoreWebView2 is null || !Directory.Exists(Path.Combine(output, "web-profile", otherId))) throw new Exception("Different Ely account reused the previous website profile");
                        Console.WriteLine("PASS Ely website profiles are isolated when switching game accounts.");
                    }
                    await SkinUiChecks.RunAsync(main, panel, settings, output);
                    main.Close();
                    Console.WriteLine("PASS Ely UI: two window sizes, disconnected/connected state, isolated providers, Ely head, English, keyboard focus and two-factor recovery.");
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Ely UI failed", failure);
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static Task InvokeAsync(object target, string name) => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private static void RemoveLoaded(Window window)
    {
        var store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
        if (store is null) return;
        var handlers = (RoutedEventHandlerInfo[]?)store.GetType().GetMethod("GetRoutedEventHandlers")!.Invoke(store, new object[] { FrameworkElement.LoadedEvent });
        foreach (var info in handlers ?? []) window.RemoveHandler(FrameworkElement.LoadedEvent, info.Handler);
    }
    private static void Prepare(Window window) { window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -5000; window.Top = -5000; }
    private static async Task Settle(Window window) { window.UpdateLayout(); await Task.Delay(280); window.UpdateLayout(); }
    private static void Capture(Window window, string output, string name)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 1.5), (int)Math.Ceiling(visual.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    private sealed class ChallengeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"errorMessage\":\"Account protected with two factor auth.\"}") });
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                using var data = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { accessToken = "fixture-access", clientToken = data.RootElement.GetProperty("clientToken").GetString(), selectedProfile = new { id = "ffc8fdc95824509e8a57c99b940fb996", name = "OrbitTest" } })) };
            }
            var pixels = new byte[64 * 64 * 4];
            for (int y = 8; y < 16; y++) for (int x = 8; x < 16; x++) { var offset = (y * 64 + x) * 4; pixels[offset] = 140; pixels[offset + 1] = 190; pixels[offset + 2] = 240; pixels[offset + 3] = 255; }
            var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = new MemoryStream(); encoder.Save(output);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(output.ToArray()) };
        }
    }
}
