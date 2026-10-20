using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OrbitLauncher.Models;
using OrbitLauncher.Services.Connect;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class OrbitConnectDialog : Window
{
    private readonly GameInstance _instance;
    private readonly CancellationTokenSource _closed = new();
    private ConnectWorkerClient? _client;
    private ConnectStatus _status = new("idle", "Откройте мир для сети или вставьте приглашение друга.");
    private bool _busy;
    public OrbitConnectDialog(GameInstance instance, bool startWorker = true)
    {
        InitializeComponent(); _instance = instance; InstanceText.Text = instance.Name;
        LocalizationService.Apply(this);
        if (startWorker && instance.OrbitConnectEnabled) Loaded += async (_, _) => await PollAsync();
        else if (startWorker)
        {
            StatusText.Text = LocalizationService.Translate("Включите Orbit Connect в параметрах сборки и перезапустите Minecraft.");
            JoinButton.IsEnabled = false; InviteBox.IsEnabled = false; PortBox.IsEnabled = false; HostButton.IsEnabled = false;
        }
        Closed += (_, _) => _closed.Cancel();
    }
    private async Task PollAsync()
    {
        try
        {
            _client = await ConnectWorkerClient.ForAsync(_instance, _closed.Token);
            while (!_closed.IsCancellationRequested)
            {
                if (!_busy) Render(await _client.RequestAsync("state", token: _closed.Token));
                await Task.Delay(1000, _closed.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed.IsCancellationRequested) Render(new("error", "Не удалось связаться с Orbit Connect. Закройте и откройте панель для повтора.")); }
    }
    private void Render(ConnectStatus status)
    {
        _status = status;
        StatusText.Text = LocalizationService.Translate(status.Message);
        PhaseText.Text = LocalizationService.Translate(status.Phase switch
        { "hosting" => "Мир открыт", "connected" => "Подключено", "starting" or "connecting" => "Подключение…", "recovering" => "Восстанавливаем маршрут…", "error" => "Не удалось подключиться", _ => "Ожидание" });
        PhaseText.SetResourceReference(ForegroundProperty, status.Phase == "error" ? "WarningBrush" : "TextBrush");
        CopyButton.IsEnabled = status.Invitation.Length > 0;
        CodeBox.Text = status.Invitation; CodeBox.Visibility = CopyButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        AddressButton.Visibility = status.Phase == "connected" && status.Address.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddressButton.Content = status.Address + "  ·  " + LocalizationService.Translate("Скопировать адрес");
        PingText.Visibility = status.PingMs is null ? Visibility.Collapsed : Visibility.Visible;
        PingText.Text = status.PingMs is double ping ? $"{Math.Round(ping)} ms" : "";
        bool preparing = status.Phase is "starting" or "connecting";
        JoinButton.IsEnabled = !preparing && !_busy; StopButton.IsEnabled = status.Phase is not "idle";
    }
    private async Task SendAsync(string path, object? body = null)
    {
        if (_busy) return;
        _busy = true; JoinButton.IsEnabled = false;
        try { _client ??= await ConnectWorkerClient.ForAsync(_instance, _closed.Token); Render(await _client.RequestAsync(path, body, _closed.Token)); }
        catch (OperationCanceledException) { }
        catch (Exception) { Render(new("error", "Не удалось связаться с Orbit Connect. Закройте и откройте панель для повтора.")); }
        finally { _busy = false; if (!_closed.IsCancellationRequested) Render(_status); }
    }
    private async void Join_Click(object sender, RoutedEventArgs e) => await SendAsync("join", new { code = InviteBox.Text.Trim() });
    private async void Invite_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SendAsync("join", new { code = InviteBox.Text.Trim() }); } }
    private async void Host_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out int port) || port is < 1 or > 65535)
        { Render(new("error", "Введите LAN-порт, показанный в чате Minecraft.")); return; }
        await SendAsync("host", new { port });
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await SendAsync("stop");
    private void Copy_Click(object sender, RoutedEventArgs e) { Copy(_status.Invitation); }
    private void Address_Click(object sender, RoutedEventArgs e) { Copy(_status.Address); }
    private void Copy(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); StatusText.Text = LocalizationService.Translate("Скопировано."); }
        catch (System.Runtime.InteropServices.COMException) { StatusText.Text = LocalizationService.Translate("Буфер обмена занят. Выделите текст и нажмите Ctrl+C."); }
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Credits_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/burningtnt/Terracotta#许可") { UseShellExecute = true }); }
        catch (Exception) { StatusText.Text = "github.com/burningtnt/Terracotta · AGPL-3.0 + IPC exception"; }
    }
}
