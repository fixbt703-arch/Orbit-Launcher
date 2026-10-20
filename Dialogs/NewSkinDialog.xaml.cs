using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Microsoft.Win32;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class NewSkinDialog : Window
{


	public string SkinName => NameBox.Text.Trim();

	public string SkinFilePath { get; private set; } = string.Empty;

	public string SkinVariant
	{
		get
		{
			if (SlimModelRadio.IsChecked != true)
			{
				return "classic";
			}
			return "slim";
		}
	}

	public bool ApplyAfterSave { get; private set; }

	public NewSkinDialog()
	{
		InitializeComponent();
		LocalizationService.Apply(this);
		base.Loaded += delegate
		{
			NameBox.Focus();
		};
	}

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = LocalizationService.Translate("Выберите скин Minecraft"),
			Filter = "PNG (*.png)|*.png",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog(this) == true)
		{
            try
            {
                if (new FileInfo(openFileDialog.FileName).Length > 196608) throw new InvalidDataException(LocalizationService.Translate("PNG должен быть меньше 192 КБ."));
                MinecraftSkinService.ValidateSkin(File.ReadAllBytes(openFileDialog.FileName));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
            { ErrorText.Text = LocalizationService.Translate(ex.Message); return; }
            ErrorText.Text = "";
			SkinFilePath = openFileDialog.FileName;
			FileBox.Text = Path.GetFileName(openFileDialog.FileName);
			if (string.IsNullOrWhiteSpace(NameBox.Text))
			{
				NameBox.Text = Path.GetFileNameWithoutExtension(openFileDialog.FileName)[..Math.Min(28, Path.GetFileNameWithoutExtension(openFileDialog.FileName).Length)];
            }
            UpdatePreview();
        }
    }

    private void Model_Changed(object sender, RoutedEventArgs e) { if (IsInitialized) UpdatePreview(); }
    private void UpdatePreview()
    {
        if (SkinPreview is null || PreviewHint is null || !File.Exists(SkinFilePath)) return;
        try
        {
            SkinPreview.Source = SkinLibraryService.CreateFrontPreview(MinecraftSkinService.DecodeSkin(File.ReadAllBytes(SkinFilePath)), SkinVariant == "slim");
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            ErrorText.Text = LocalizationService.Translate(ex.Message);
            SkinPreview.Source = null; PreviewHint.Visibility = Visibility.Visible;
        }
    }

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		Complete(apply: false);
	}

	private void SaveAndApply_Click(object sender, RoutedEventArgs e)
	{
		Complete(apply: true);
	}

	private void Complete(bool apply)
	{
		if (string.IsNullOrWhiteSpace(SkinName))
		{
			ErrorText.Text = LocalizationService.Translate("Введите название скина.");
			NameBox.Focus();
			return;
		}
		if (string.IsNullOrWhiteSpace(SkinFilePath) || !File.Exists(SkinFilePath))
		{
			ErrorText.Text = LocalizationService.Translate("Выберите PNG-файл скина.");
			return;
		}
		try
		{
			MinecraftSkinService.ValidateSkin(File.ReadAllBytes(SkinFilePath));
		}
		catch (Exception ex)
		{
			ErrorText.Text = LocalizationService.Translate(ex.Message);
			return;
		}
		ApplyAfterSave = apply;
		base.DialogResult = true;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}








}
