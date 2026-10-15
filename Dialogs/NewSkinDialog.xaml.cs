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
			SkinFilePath = openFileDialog.FileName;
			FileBox.Text = Path.GetFileName(openFileDialog.FileName);
			if (string.IsNullOrWhiteSpace(NameBox.Text))
			{
				NameBox.Text = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
			}
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
			LocalizationService.Show(this, "Введите название скина.", "Новый скин", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			NameBox.Focus();
			return;
		}
		if (string.IsNullOrWhiteSpace(SkinFilePath) || !File.Exists(SkinFilePath))
		{
			LocalizationService.Show(this, "Выберите PNG-файл скина.", "Новый скин", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		try
		{
			MinecraftSkinService.ValidateSkin(File.ReadAllBytes(SkinFilePath));
		}
		catch (Exception ex)
		{
			LocalizationService.Show(this, ex.Message, "Новый скин", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
