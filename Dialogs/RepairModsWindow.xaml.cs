using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class RepairModsWindow : Window
{
    private readonly GameInstance _instance;
    private readonly ModCompatibilityRepairService _service;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _running = true;

    public ObservableCollection<RepairLine> Updates { get; } = [];

    public RepairModsWindow(GameInstance instance, HttpClient httpClient, string modrinthApiBase,
        string curseForgeApiBase, string curseForgeApiKey)
    {
        _instance = instance;
        _service = new ModCompatibilityRepairService(httpClient, modrinthApiBase,
            curseForgeApiBase: curseForgeApiBase, curseForgeApiKey: curseForgeApiKey);
        InitializeComponent();
        LocalizationService.Apply(this);
        DataContext = this;
        InstanceText.Text = $"{instance.Name}  •  Minecraft {instance.GameVersion}  •  {instance.Loader}";
        Loaded += RepairModsWindow_Loaded;
    }

    private async void RepairModsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= RepairModsWindow_Loaded;
        try
        {
            var progress = new Progress<RepairUpdate>(update =>
            {
                PhaseText.Text = update.State == RepairState.Checking ? $"{update.File}  ·  {update.Message}" : L("Проверяем и исправляем моды…", "Checking and repairing mods…");
                ProgressText.Text = $"{update.Completed} / {update.Total}";
                RepairProgressBar.IsIndeterminate = update.State == RepairState.Checking && update.Completed == 0 && update.Total > 0;
                var done = update.State == RepairState.Checking ? Math.Max(0, update.Completed - 1) : update.Completed;
                RepairProgressBar.Value = update.Total == 0 ? 100 : done * 100d / update.Total;
                if (update.State != RepairState.Checking)
                    Updates.Add(new RepairLine(update.File, update.Message, update.State));
            });
            var result = await _service.RepairAsync(_instance, progress, _cancellation.Token);
            RepairProgressBar.Value = 100;
            PhaseText.Text = result.NeedsAttention == 0 ? L("Проверка завершена", "Scan complete") : L("Проверка завершена — есть замечания", "Scan complete — review needed");
            SummaryText.Text = LocalizationService.IsEnglish
                ? $"Checked: {result.Checked}  •  Replaced: {result.Replaced}  •  Added: {result.Installed}  •  Disabled: {result.Disabled}  •  Need review: {result.NeedsAttention}"
                : $"Проверено: {result.Checked}  •  Заменено: {result.Replaced}  •  Добавлено: {result.Installed}  •  Отключено: {result.Disabled}  •  Требуют внимания: {result.NeedsAttention}";
            if (result.Checked == 0)
                Updates.Add(new RepairLine(L("Моды не найдены", "No mods found"), L("В этой сборке нет включённых JAR-файлов.", "This instance has no enabled JAR files."), RepairState.Compatible));
        }
        catch (OperationCanceledException)
        {
            PhaseText.Text = L("Проверка остановлена", "Scan stopped");
            SummaryText.Text = L("Уже завершённые замены сохранены. Текущая загрузка отменена.", "Completed replacements were kept. The current download was canceled.");
        }
        catch (Exception ex)
        {
            PhaseText.Text = L("Проверка не выполнена", "Scan failed");
            SummaryText.Text = ex.Message;
            Updates.Add(new RepairLine(L("Ошибка", "Error"), ex.Message, RepairState.Failed));
        }
        finally
        {
            _running = false;
            RepairProgressBar.IsIndeterminate = false;
            ActionButton.IsEnabled = true;
            ActionButton.Content = L("Закрыть", "Close");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _cancellation.Cancel();
            ActionButton.IsEnabled = false;
            ActionButton.Content = L("Останавливаем…", "Stopping…");
        }
        else Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            _cancellation.Cancel();
            ActionButton.IsEnabled = false;
            ActionButton.Content = L("Останавливаем…", "Stopping…");
        }
        base.OnClosing(e);
    }

    private static string L(string ru, string en) => LocalizationService.IsEnglish ? en : ru;
}

public sealed class RepairLine(string file, string message, RepairState state)
{
    public string File { get; } = file;
    public string Message { get; } = message;
    public string Glyph { get; } = state switch
    {
        RepairState.Replaced => "\uEA5E", RepairState.Installed => "\uEB0B",
        RepairState.Compatible => "\uEA5E", RepairState.Disabled or RepairState.NeedsAttention => "\uEA06", _ => "\uEB13"
    };
    public string Tone { get; } = state switch
    {
        RepairState.Replaced or RepairState.Installed or RepairState.Compatible => "Success",
        RepairState.Disabled or RepairState.NeedsAttention => "Warning", RepairState.Checking => "Muted", _ => "Danger"
    };
}
