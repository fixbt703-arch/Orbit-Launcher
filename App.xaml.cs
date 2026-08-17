using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using OrbitLauncher.Utilities;

namespace OrbitLauncher;

public partial class App : Application
{
    private static bool _showingUnhandledError;

    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationService.SetLanguage(ReadSavedLanguage());
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static string ReadSavedLanguage()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
                return "ru";
            using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            return document.RootElement.TryGetProperty("language", out var language)
                ? language.GetString() ?? "ru"
                : "ru";
        }
        catch
        {
            return "ru";
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_showingUnhandledError)
            return;

        try
        {
            _showingUnhandledError = true;
            var message = e.Exception.InnerException?.Message ?? e.Exception.Message;
            MessageBox.Show(
                $"{LocalizationService.Translate("Orbit Launcher столкнулся с ошибкой:")}\n\n{message}",
                "Orbit Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _showingUnhandledError = false;
        }
    }
}
