using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Controls;

public partial class ElySkinsPanel : UserControl
{
    private ElyAccountService? _accounts;
    private ElySkinService? _skins;
    private bool _refreshing, _opening, _disposed;
    private int _portalRevision;
    private readonly CancellationTokenSource _lifetime = new();
    private byte[]? _pendingSkin;
    private string _page = "https://ely.by/skins";
    private string _browserProfile = Path.Combine(AppPaths.Root, "ely-web");
    private string? _portalAccountId;
    private readonly System.Windows.Threading.DispatcherTimer _navigationTimeout = new() { Interval = TimeSpan.FromSeconds(30) };
    public event EventHandler? AccountChanged;
    public ElySkinsPanel()
    {
        InitializeComponent();
        _navigationTimeout.Tick += (_, _) =>
        {
            _navigationTimeout.Stop(); Browser.CoreWebView2?.Stop(); Browser.Visibility = Visibility.Collapsed;
            BrowserStatus.Visibility = Visibility.Visible;
            BrowserStatus.Text = T("Ely.by долго не отвечает. Нажмите «Повторить».", "Ely.by is taking too long to respond. Select Retry.");
        };
    }
    public void Initialize(ElyAccountService accounts, ElySkinService skins, string? browserProfile = null)
    {
        if (browserProfile is not null) _browserProfile = browserProfile;
        _accounts = accounts; _skins = skins; UpdateState();
    }
    public void UpdateState()
    {
        var account = _accounts?.Account;
        ConnectButton.Visibility = account is null ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
        AccountTitle.Text = account is null ? T("Аккаунт Ely.by", "Ely.by account") : account.Name + " · Ely.by";
        AccountHint.Text = account is null ? T("Не подключён · ник и скин настраиваются отдельно от Microsoft.", "Not connected · your name and skin are separate from Microsoft.") : T("Подключён · этот ник и скин используются при запуске игры.", "Connected · this name and skin are used when launching the game.");
        PreviewName.Text = account?.Name ?? T("Нет предпросмотра", "No preview yet");
        SkinHeading.Text = account is null ? T("Подключите свой образ", "Connect your Minecraft look") : T("Ваш образ в Minecraft", "Your Minecraft look");
        SkinDescription.Text = account is null ? T("Войдите в Ely.by, чтобы загрузить PNG или выбрать готовый скин. Ваш аккаунт Microsoft останется отдельно.", "Sign in to Ely.by to upload a PNG or choose a skin. Your Microsoft account stays separate.") : T("Загрузите свой PNG или выберите готовый скин. На сайте нажмите «Надеть», чтобы применить его к этому аккаунту.", "Upload a PNG or choose a skin. Select Wear on the website to apply it to this account.");
        SkinActions.Visibility = RefreshButton.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.IsEnabled = !_refreshing;
        var head = account is null ? null : _skins?.GetCachedHead(account.Name);
        HeadPreview.Source = head?.Image;
        HeadPreview.Visibility = head is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewFallback.Visibility = head is null ? Visibility.Visible : Visibility.Collapsed;
        LocalizationService.Apply(this);
    }
    public bool Connect()
    {
        if (_accounts is null) return false;
        if (_accounts.IsSignedIn) return true;
        var dialog = new ElyLoginWindow(_accounts) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return false;
        UpdateState(); AccountChanged?.Invoke(this, EventArgs.Empty); _ = RefreshAsync(); return true;
    }
    private void Connect_Click(object sender, RoutedEventArgs e) => Connect();
    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_accounts is null) return;
        DisconnectButton.IsEnabled = false;
        try
        {
            await _accounts.SignOutAsync();
            _pendingSkin = null; ++_portalRevision; _navigationTimeout.Stop();
            StatusPanel.Visibility = Visibility.Collapsed;
            Portal.Visibility = Visibility.Collapsed; Overview.Visibility = AccountStrip.Visibility = Visibility.Visible;
            UpdateState(); AccountChanged?.Invoke(this, EventArgs.Empty);
            if (Browser.CoreWebView2 is { } web) await web.Profile.ClearBrowsingDataAsync();
        }
        catch { SetStatus(T("Не удалось завершить выход. Повторите попытку.", "Couldn't finish signing out. Please try again."), true); }
        finally { DisconnectButton.IsEnabled = true; }
    }
    public async Task RefreshAsync()
    {
        if (_disposed || _refreshing || _accounts?.Account is not { } account || _skins is null) return;
        _refreshing = true; RefreshButton.IsEnabled = false; SkinProgress.Visibility = Visibility.Visible;
        SetStatus(T("Обновление скина…", "Refreshing skin…"));
        try
        {
            var head = await _skins.RefreshHeadAsync(account.Name, _lifetime.Token);
            if (_accounts.Account?.Id != account.Id) return;
            UpdateState();
            SetStatus(head is null ? T("На Ely.by ещё не выбран скин.", "No skin selected on Ely.by yet.") : T("Текущий скин загружен с Ely.by.", "Current skin loaded from Ely.by."));
            AccountChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch { if (_accounts?.Account?.Id == account.Id) SetStatus(T("Не удалось обновить скин. Проверьте соединение и нажмите «Обновить скин».", "Couldn't refresh the skin. Check your connection and select Refresh skin."), true); }
        finally { _refreshing = false; RefreshButton.IsEnabled = true; SkinProgress.Visibility = Visibility.Collapsed; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Upload_Click(object sender, RoutedEventArgs e) => await ChooseAndUploadAsync();
    public async Task ChooseAndUploadAsync()
    {
        if (!Connect()) return;
        var file = new OpenFileDialog { Filter = "PNG (*.png)|*.png", Title = T("Выберите скин", "Choose skin") };
        if (file.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var info = new FileInfo(file.FileName);
            if (info.Length > 196608) throw new InvalidDataException(T("PNG должен быть меньше 192 КБ.", "The PNG must be smaller than 192 KB."));
            var bytes = await File.ReadAllBytesAsync(file.FileName);
            MinecraftSkinService.ValidateSkin(bytes);
            _pendingSkin = bytes;
            await OpenPortalAsync("https://ely.by/skins/add");
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }
    private async void Manage_Click(object sender, RoutedEventArgs e)
    {
        if (Connect()) await OpenPortalAsync("https://ely.by/skins");
    }
    private async Task OpenPortalAsync(string page)
    {
        if (_disposed || _opening) return;
        int revision = ++_portalRevision;
        _opening = true; _page = page;
        RuntimeButton.Visibility = Visibility.Collapsed;
        Overview.Visibility = AccountStrip.Visibility = Visibility.Collapsed; Portal.Visibility = Visibility.Visible;
        BrowserStatus.Text = T("Подключение к Ely.by…", "Connecting to Ely.by…"); BrowserStatus.Visibility = Visibility.Visible;
        try
        {
            var accountId = _accounts?.Account?.Id ?? throw new InvalidOperationException("Ely.by account required");
            if (Browser.CoreWebView2 is not null && _portalAccountId != accountId)
            {
                // A different game login must not inherit another Ely.by account's website cookies.
                var parent = (Grid)Browser.Parent;
                int index = parent.Children.IndexOf(Browser);
                Browser.CoreWebView2.NavigationStarting -= NavigationStarting;
                Browser.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                var replacement = new Microsoft.Web.WebView2.Wpf.WebView2 { DefaultBackgroundColor = Browser.DefaultBackgroundColor, Visibility = Visibility.Collapsed };
                Browser.Dispose(); parent.Children.Remove(Browser);
                UnregisterName("Browser"); Browser = replacement; RegisterName("Browser", replacement);
                parent.Children.Insert(index, replacement);
            }
            if (Browser.CoreWebView2 is null)
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_browserProfile, accountId));
                await Browser.EnsureCoreWebView2Async(environment);
                if (_disposed || revision != _portalRevision) return;
                _portalAccountId = accountId;
                var web = Browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 unavailable");
                web.Settings.AreDevToolsEnabled = false;
                web.Settings.AreHostObjectsAllowed = false;
                web.Settings.IsWebMessageEnabled = false;
                web.Settings.AreDefaultContextMenusEnabled = false;
                web.NavigationStarting += NavigationStarting;
                web.NavigationCompleted += NavigationCompleted;
                web.NewWindowRequested += (_, args) => { args.Handled = true; if (IsAllowed(args.Uri)) web.Navigate(args.Uri); };
                web.DownloadStarting += (_, args) => args.Cancel = true;
                web.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
                web.ProcessFailed += (_, _) => { Browser.Visibility = Visibility.Collapsed; BrowserStatus.Visibility = Visibility.Visible; BrowserStatus.Text = T("Окно Ely.by закрылось. Нажмите «Повторить».", "Ely.by closed unexpectedly. Select Retry."); };
            }
            if (_disposed || revision != _portalRevision) return;
            BackButton.Focus();
            Browser.Visibility = Visibility.Visible;
            PortalHint.Text = T("Войдите на сайте как ", "Sign in on the website as ") + _accounts?.Account?.Name + T(". Выберите скин и нажмите «Надеть».", ". Choose a skin and select Wear.");
            Browser.CoreWebView2.Navigate(page);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Browser.Visibility = Visibility.Collapsed;
            RuntimeButton.Visibility = Visibility.Visible;
            BrowserStatus.Text = T("Не удалось открыть Ely.by. Нужен установленный Microsoft Edge WebView2 Runtime. После установки нажмите «Повторить».", "Couldn't open Ely.by. Microsoft Edge WebView2 Runtime is required. Install it, then select Retry.");
        }
        catch
        {
            Browser.Visibility = Visibility.Collapsed;
            BrowserStatus.Text = T("Не удалось открыть окно Ely.by. Нажмите «Повторить» или перезапустите лаунчер.", "Couldn't open Ely.by. Select Retry or restart the launcher.");
        }
        finally { _opening = false; }
    }
    public static bool IsAllowed(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "ely.by" || uri.Host == "account.ely.by" || uri.Host == "authserver.ely.by");
    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!IsAllowed(args.Uri)) { args.Cancel = true; return; }
        _navigationTimeout.Stop(); _navigationTimeout.Start();
    }
    private async void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _navigationTimeout.Stop();
        if (!args.IsSuccess)
        {
            Browser.Visibility = Visibility.Collapsed; BrowserStatus.Visibility = Visibility.Visible;
            BrowserStatus.Text = T("Ely.by не загрузился. Проверьте соединение и нажмите «Повторить».", "Ely.by didn't load. Check your connection and select Retry."); return;
        }
        BrowserStatus.Visibility = Visibility.Collapsed;
        if (_pendingSkin is not { } png || Browser.Source is not { Host: "ely.by", AbsolutePath: "/skins/add" }) return;
        try
        {
            // Feed only the PNG explicitly chosen by the user into Ely.by's original file input.
            // The site's own code performs upload and shows the real result; it retains the Wear action.
            string data = JsonSerializer.Serialize(Convert.ToBase64String(png));
            string result = await Browser.ExecuteScriptAsync("(() => { const input=document.querySelector('#skinSelector'); if(!input) return false; const bytes=Uint8Array.from(atob(" + data + "),c=>c.charCodeAt(0)); const transfer=new DataTransfer(); transfer.items.add(new File([bytes],'skin.png',{type:'image/png'})); input.files=transfer.files; input.dispatchEvent(new Event('change',{bubbles:true})); return true; })()");
            if (result == "true") { _pendingSkin = null; PortalHint.Text = T("PNG передан в Ely.by. После загрузки нажмите «Надеть». Если сайт попросит вход — используйте ", "PNG sent to Ely.by. After upload, select Wear. If asked to sign in, use ") + _accounts?.Account?.Name + "."; }
        }
        catch { PortalHint.Text = T("Выберите PNG через кнопку загрузки на Ely.by.", "Choose the PNG using Ely.by's upload button."); }
    }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await OpenPortalAsync(_page);
    private void Runtime_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }); }
        catch { BrowserStatus.Text = T("Не удалось открыть браузер. Установите Microsoft Edge WebView2 Runtime и повторите.", "Couldn't open the browser. Install Microsoft Edge WebView2 Runtime and retry."); }
    }
    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        ++_portalRevision; _pendingSkin = null; _navigationTimeout.Stop(); Browser.CoreWebView2?.Stop();
        Portal.Visibility = Visibility.Collapsed; Overview.Visibility = AccountStrip.Visibility = Visibility.Visible;
        if (_accounts?.IsSignedIn == true) UploadButton.Focus(); else ConnectButton.Focus();
        await RefreshAsync();
    }
    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text; StatusPanel.Visibility = Visibility.Visible;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "TextBrush");
    }
    public void Dispose() { if (_disposed) return; _disposed = true; ++_portalRevision; _lifetime.Cancel(); _navigationTimeout.Stop(); _pendingSkin = null; Browser.Dispose(); }
    private static string T(string ru, string en) => LocalizationService.IsEnglish ? en : ru;
}
