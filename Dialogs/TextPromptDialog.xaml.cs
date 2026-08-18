using System.Windows;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class TextPromptDialog : Window
{
    public string ResultText => ValueBox.Text;

    public TextPromptDialog(string title, string label, string initialValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        LabelText.Text = label;
        ValueBox.Text = initialValue;
        LocalizationService.Apply(this);
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
