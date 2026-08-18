using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class NewSkinDialog : Window
{
    public string SkinName => NameBox.Text.Trim();
    public string SkinFilePath { get; private set; } = string.Empty;
    public string SkinVariant => SlimModelRadio.IsChecked == true ? "slim" : "classic";
    public bool ApplyAfterSave { get; private set; }

    public NewSkinDialog()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Translate("Выберите скин Minecraft"),
            Filter = "PNG (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        SkinFilePath = dialog.FileName;
        FileBox.Text = Path.GetFileName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Complete(apply: false);
    private void SaveAndApply_Click(object sender, RoutedEventArgs e) => Complete(apply: true);

    private void Complete(bool apply)
    {
        if (string.IsNullOrWhiteSpace(SkinName))
        {
            LocalizationService.Show(this, "Введите название скина.", "Новый скин", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(SkinFilePath) || !File.Exists(SkinFilePath))
        {
            LocalizationService.Show(this, "Выберите PNG-файл скина.", "Новый скин", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            MinecraftSkinService.ValidateSkin(File.ReadAllBytes(SkinFilePath));
        }
        catch (Exception ex)
        {
            LocalizationService.Show(this, ex.Message, "Новый скин", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ApplyAfterSave = apply;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
