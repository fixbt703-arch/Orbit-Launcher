using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher;

public partial class MainWindow
{
    private bool _zapretRepairInProgress;
    private string? _zapretFolder;
    private int _zapretStatusRevision;

    private async Task RefreshZapretCompatibilityAsync()
    {
        var revision = ++_zapretStatusRevision;
        var folder = _zapretFolder ?? _settings.ZapretFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Zapret");
        var present = false;
        try { present = await Task.Run(() => ZapretCompatibilityService.HasCompatibilityAsync(folder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
        if (revision == _zapretStatusRevision)
            ZapretRemoveButton.Visibility = present ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ZapretRepair_Click(object sender, RoutedEventArgs e) => await ChangeZapretCompatibilityAsync(removing: false);

    private async void ZapretRemove_Click(object sender, RoutedEventArgs e) => await ChangeZapretCompatibilityAsync(removing: true);

    private async Task ChangeZapretCompatibilityAsync(bool removing)
    {
        if (_zapretRepairInProgress) return;
        _zapretRepairInProgress = true;
        ++_zapretStatusRevision;
        ZapretRepairButton.IsEnabled = false;
        ZapretRemoveButton.IsEnabled = false;
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var defaultFolder = _zapretFolder ?? _settings.ZapretFolder ?? Path.Combine(desktop, "Zapret");
            var dialog = new OpenFolderDialog
            {
                Title = LocalizationService.Translate("Выберите папку Zapret"),
                InitialDirectory = Directory.Exists(defaultFolder) ? defaultFolder : desktop,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            _zapretFolder = dialog.FolderName;

            ZapretBackupPanel.Visibility = Visibility.Collapsed;
            ZapretBackupPathText.Text = string.Empty;
            ShowZapretRepairStatus(removing ? "Удаляем исключения Orbit и сохраняем резервную копию…" : "Сохраняем исключения и резервную копию…");
            var result = removing
                ? await ZapretCompatibilityService.RemoveAsync(dialog.FolderName)
                : await ZapretCompatibilityService.ApplyAsync(dialog.FolderName);
            _settings.ZapretFolder = dialog.FolderName;
            await _settingsService.SaveAsync(_settings);
            ShowZapretCompatibilityResult(removing, result);
        }
        catch (Exception ex)
        {
            ShowZapretRepairStatus(ex switch
            {
                ZapretCompatibilityOwnershipException =>
                    "Не удалось безопасно определить старые записи Orbit. Список не изменён. Резервные копии сохранены в данных Orbit.",
                DirectoryNotFoundException or FileNotFoundException =>
                    "Не найдены файлы Zapret. Выберите его основную папку с папкой lists и повторите попытку.",
                InvalidDataException or System.Text.DecoderFallbackException =>
                    "Не удалось прочитать список исключений Zapret. Проверьте формат файла и повторите попытку.",
                UnauthorizedAccessException =>
                    "Нет доступа к файлам Zapret или резервным копиям Orbit. Проверьте права доступа и повторите попытку.",
                IOException =>
                    "Не удалось сохранить список. Закройте его в редакторе, проверьте свободное место и повторите попытку.",
                _ => "Не удалось изменить совместимость. Проверьте папку Zapret и повторите попытку."
            });
        }
        finally
        {
            await RefreshZapretCompatibilityAsync();
            _zapretRepairInProgress = false;
            ZapretRepairButton.IsEnabled = true;
            ZapretRemoveButton.IsEnabled = true;
        }
    }

    private void ShowZapretCompatibilityResult(bool removing, ZapretCompatibilityResult result)
    {
        ShowZapretRepairStatus(removing
            ? result.Changed
                ? "Исключения Orbit удалены. Ваши записи сохранены. Полностью перезапустите Zapret."
                : "Исключения Orbit не найдены. Ваши записи оставлены без изменений."
            : result.Changed
                ? "Исключения сохранены. Полностью перезапустите Zapret, затем повторите загрузку или применение скина."
                : "Исключения уже настроены. Полностью перезапустите Zapret, затем повторите загрузку или применение скина.");
        ZapretBackupPathText.Text = result.BackupPath ?? string.Empty;
        ZapretBackupPanel.Visibility = string.IsNullOrEmpty(result.BackupPath) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowZapretRepairStatus(string message)
    {
        ZapretRepairStatusText.Text = LocalizationService.Translate(message);
        ZapretRepairStatusText.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ZapretRepairStatusText.BringIntoView()));
    }
}
