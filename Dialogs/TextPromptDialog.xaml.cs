using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
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
		base.Loaded += delegate
		{
			ValueBox.Focus();
			ValueBox.SelectAll();
		};
	}

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = true;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}








}
