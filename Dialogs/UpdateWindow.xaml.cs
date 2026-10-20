using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class UpdateWindow : Window
{
    private readonly GitHubUpdateService _service;
    private readonly LauncherUpdate _update;
    private readonly Func<LauncherUpdate, string, CancellationToken, Task> _install;
    private CancellationTokenSource? _download;
    private bool _closed;
    private bool _handingOff;
    private bool _busy;
    private string? _downloadedFile;
    public bool InstallationStarted { get; private set; }

    public UpdateWindow(GitHubUpdateService service, LauncherUpdate update,
        Func<LauncherUpdate, string, CancellationToken, Task> install)
    {
        InitializeComponent();
        _service = service; _update = update; _install = install;
        var en = LocalizationService.IsEnglish;
        Title = en ? "Update Orbit Launcher" : "Обновление Orbit Launcher";
        HeadingText.Text = en ? "An update is available" : "Доступно обновление";
        VersionText.Text = en ? $"Version {GitHubUpdateService.DisplayVersion(GitHubUpdateService.CurrentVersion)} → {GitHubUpdateService.DisplayVersion(update.Version)}"
            : $"Версия {GitHubUpdateService.DisplayVersion(GitHubUpdateService.CurrentVersion)} → {GitHubUpdateService.DisplayVersion(update.Version)}";
        NotesLabel.Text = en ? "What's new" : "Что нового";
        NotesText.Text = string.IsNullOrWhiteSpace(update.Notes) ? (en ? "The author hasn't added release notes." : "Автор пока не добавил описание изменений.") : update.Notes;
        ReleasePageButton.Content = en ? "On GitHub" : "На GitHub";
        CloseButton.ToolTip = en ? "Close" : "Закрыть";
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, CloseButton.ToolTip.ToString());
        System.Windows.Automation.AutomationProperties.SetName(DownloadProgress, en ? "Update download" : "Загрузка обновления");
        MaxWidth = SystemParameters.WorkArea.Width * .94;
        MaxHeight = SystemParameters.WorkArea.Height * .92;
        ResetActions();
        Loaded += (_, _) =>
        {
            LaterButton.Focus(); // Enter must not unexpectedly start an update on opening.
            if (InterfaceMotion.CanAnimate)
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; RequestClose(); } };
        Closing += (_, e) => { if (_handingOff) e.Cancel = true; else _download?.Cancel(); };
        Closed += (_, _) => { _closed = true; _download?.Cancel(); };
    }

    private void ResetActions()
    {
        var en = LocalizationService.IsEnglish;
        UpdateButton.Content = en ? "Update now" : "Обновить сейчас";
        LaterButton.Content = en ? "Later" : "Позже";
        HintText.Text = en ? "The launcher will reopen after installation. Instances and accounts are kept."
            : "После установки лаунчер откроется снова. Сборки и аккаунты сохранятся.";
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed) return;
        _busy = true;
        _download = new CancellationTokenSource();
        var token = _download.Token;
        UpdateButton.IsEnabled = false;
        LaterButton.Content = LocalizationService.IsEnglish ? "Cancel" : "Отменить";
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        ProgressValue.Text = "";
        ProgressLabel.Text = LocalizationService.IsEnglish ? "Downloading update…" : "Загружаем обновление…";
        HintText.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        HintText.Text = LocalizationService.IsEnglish ? "You can cancel the download and continue using the launcher." : "Можно отменить загрузку и продолжить пользоваться лаунчером.";
        try
        {
            _downloadedFile = await _service.DownloadAsync(_update, new Progress<UpdateDownloadProgress>(value =>
            {
                if (_closed || !_busy || token.IsCancellationRequested) return;
                DownloadProgress.Value = value.Percent;
                ProgressValue.Text = $"{value.Percent:0}% · {value.Received / 1048576d:0.0} / {value.Total / 1048576d:0.0} MB";
            }), token);
            if (_closed) return;
            token.ThrowIfCancellationRequested();
            _handingOff = true;
            CloseButton.IsEnabled = LaterButton.IsEnabled = false;
            ProgressLabel.Text = LocalizationService.IsEnglish ? "Starting installer…" : "Запускаем установщик…";
            await _install(_update, _downloadedFile, token);
            InstallationStarted = true;
            _handingOff = false;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (_closed) return;
            ProgressLabel.Text = LocalizationService.IsEnglish ? "Download cancelled" : "Загрузка отменена";
            HintText.Text = LocalizationService.IsEnglish ? "The launcher hasn't been changed. You can try again." : "Лаунчер не изменён. Можно попробовать ещё раз.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            ProgressLabel.Text = LocalizationService.IsEnglish ? "Couldn't install the update" : "Не удалось установить обновление";
            HintText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
            HintText.Text = LocalizationService.IsEnglish ? "The launcher hasn't been changed. Retry, or open the release on GitHub."
                : (ex is InvalidDataException or InvalidOperationException ? ex.Message : "Не удалось загрузить или запустить обновление. Проверьте подключение и попробуйте ещё раз.");
        }
        finally
        {
            _handingOff = false; _busy = false;
            if (!InstallationStarted && _downloadedFile is not null) _service.DiscardDownload(_downloadedFile);
            _downloadedFile = null;
            _download.Dispose(); _download = null;
            if (!_closed)
            {
                UpdateButton.IsEnabled = CloseButton.IsEnabled = LaterButton.IsEnabled = true;
                UpdateButton.Content = LocalizationService.IsEnglish ? "Try again" : "Повторить";
                LaterButton.Content = LocalizationService.IsEnglish ? "Later" : "Позже";
            }
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) _download?.Cancel(); else RequestClose();
    }
    private void RequestClose() { if (!_handingOff) Close(); }
    private void Close_Click(object sender, RoutedEventArgs e) => RequestClose();
    private void ReleasePage_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_update.ReleaseUrl.AbsoluteUri) { UseShellExecute = true }); }
        catch { HintText.Text = LocalizationService.IsEnglish ? "Couldn't open your browser." : "Не удалось открыть браузер."; }
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.LeftButton == MouseButtonState.Pressed && !CloseButton.IsMouseOver) { try { DragMove(); } catch (InvalidOperationException) { } } }
}
