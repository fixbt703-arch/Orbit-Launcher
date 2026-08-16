using System.Windows;
using System.Windows.Threading;

namespace OrbitLauncher;

public partial class App : Application
{
    private static bool _showingUnhandledError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
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
                $"Orbit Launcher столкнулся с ошибкой:\n\n{message}",
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
