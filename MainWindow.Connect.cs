using System.Windows;
using OrbitLauncher.Dialogs;

namespace OrbitLauncher;

public partial class MainWindow
{
    private void ContextConnect_Click(object sender, RoutedEventArgs e)
    {
        if (ContextInstance(sender) is { } instance) new OrbitConnectDialog(instance) { Owner = this }.ShowDialog();
    }
}
