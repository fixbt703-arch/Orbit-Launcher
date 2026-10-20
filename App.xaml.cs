using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
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
		if (e.Args.Length == 2 && e.Args[0] == "--connect-worker")
		{
			ShutdownMode = ShutdownMode.OnExplicitShutdown;
			base.OnStartup(e);
			_ = System.Threading.Tasks.Task.Run(async () =>
			{
				try { await Services.Connect.ConnectWorkerClient.RunWorkerAsync(e.Args[1]); }
				catch (Exception) { }
				finally { await Dispatcher.InvokeAsync(() => Shutdown()); }
			});
			return;
		}
		LocalizationService.SetLanguage(ReadSavedLanguage());
		InterfaceTheme.Initialize();
		StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
		base.OnStartup(e);
		base.DispatcherUnhandledException += OnDispatcherUnhandledException;
	}

	private static string ReadSavedLanguage()
	{
		try
		{
			if (!File.Exists(AppPaths.SettingsFile))
			{
				return "ru";
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("language", out value) ? (value.GetString() ?? "ru") : "ru";
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
		{
			return;
		}
		try
		{
			_showingUnhandledError = true;
			string text = e.Exception.InnerException?.Message ?? e.Exception.Message;
			MessageBox.Show(LocalizationService.Translate("Orbit Launcher столкнулся с ошибкой:") + "\n\n" + text, "Orbit Launcher", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			_showingUnhandledError = false;
		}
	}









}
