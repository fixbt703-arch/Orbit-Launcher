using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher;

public partial class MainWindow
{
    private readonly HttpClient _updateHttp = GitHubUpdateService.CreateHttpClient();
    private readonly CancellationTokenSource _updateLifetime = new();
    private GitHubUpdateService _updateService = null!;
    private DispatcherTimer? _updateTimer;
    private LauncherUpdate? _availableUpdate;
    private Version? _dismissedUpdate;
    private UpdateWindow? _updateWindow;
    private bool _checkingUpdate;
    private bool _updatesStarted;
    private bool _updatesClosed;

    private void InitializeUpdates()
    {
        _updateService = new GitHubUpdateService(_updateHttp);
        LauncherVersionText.Text = "Orbit Launcher " + GitHubUpdateService.DisplayVersion(GitHubUpdateService.CurrentVersion);
        Activated += (_, _) => Dispatcher.BeginInvoke(new Action(() => ShowAvailableUpdate(false)), DispatcherPriority.ContextIdle);
        Closed += (_, _) =>
        {
            _updatesClosed = true;
            _updateTimer?.Stop();
            _updateLifetime.Cancel();
            _updateWindow?.Close();
            _updateHttp.Dispose();
        };
    }

    private void StartUpdateChecks()
    {
        if (_updatesStarted || _updatesClosed) return;
        _updatesStarted = true;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(false);
        _updateTimer.Start();
        _ = DelayedUpdateCheckAsync();
    }

    private async Task DelayedUpdateCheckAsync()
    {
        try { await Task.Delay(1800, _updateLifetime.Token); await CheckForUpdatesAsync(false); }
        catch (OperationCanceledException) { }
    }

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);
    private void UpdateInstall_Click(object sender, RoutedEventArgs e) => ShowAvailableUpdate(true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updatesClosed || _checkingUpdate) return;
        _checkingUpdate = true;
        UpdateCheckButton.IsEnabled = false;
        UpdateStatusText.Text = LocalizationService.IsEnglish ? "Checking GitHub…" : "Проверяем GitHub…";
        try
        {
            var update = await _updateService.CheckAsync(GitHubUpdateService.CurrentVersion, _updateLifetime.Token);
            if (_updatesClosed) return;
            _availableUpdate = update;
            UpdateInstallButton.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateStatusText.Text = update is null ? (LocalizationService.IsEnglish ? "You're up to date" : "Установлена последняя версия")
                : (LocalizationService.IsEnglish ? "Available: " : "Доступна версия ") + GitHubUpdateService.DisplayVersion(update.Version);
            ShowAvailableUpdate(manual);
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_updatesClosed)
                UpdateStatusText.Text = LocalizationService.IsEnglish ? "Couldn't check updates. Try again later."
                    : (manual && ex is InvalidDataException ? ex.Message : "Не удалось проверить обновления. Попробуйте позже.");
        }
        finally { _checkingUpdate = false; if (!_updatesClosed) UpdateCheckButton.IsEnabled = true; }
    }

    private void ShowAvailableUpdate(bool manual)
    {
        if (_updatesClosed || _availableUpdate is not { } update || _updateWindow is not null) return;
        if (!manual && (_dismissedUpdate == update.Version || !IsActive || WindowState == WindowState.Minimized)) return;
        if (OwnedWindows.Count > 0 || _launchCancellation is not null || _skinOperationInProgress)
        {
            if (manual) UpdateStatusText.Text = LocalizationService.IsEnglish ? "Finish the current operation, then update." : "Завершите текущую операцию, затем обновите лаунчер.";
            return;
        }
        _dismissedUpdate = update.Version;
        _updateWindow = new UpdateWindow(_updateService, update, StartUpdateInstallerAsync) { Owner = this };
        try
        {
            _updateWindow.ShowDialog();
            if (_updateWindow.InstallationStarted) Application.Current.Shutdown();
        }
        finally { _updateWindow = null; }
    }

    private async Task StartUpdateInstallerAsync(LauncherUpdate update, string installer, CancellationToken token)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить папку лаунчера.");
        var processId = Environment.ProcessId;
        var english = LocalizationService.IsEnglish;
        var start = await Task.Run(() => UpdateInstaller.PrepareStart(update, installer, executable, processId, english), token);
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить установщик.");
    }
}
