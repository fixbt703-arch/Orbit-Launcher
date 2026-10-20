using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Dialogs;

public partial class ElyLoginWindow : Window
{
    private readonly ElyAccountService _accounts;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    public ElyLoginWindow(ElyAccountService accounts)
    {
        _accounts = accounts;
        InitializeComponent();
        LocalizationService.Apply(this);
        Loaded += (_, _) => LoginBox.Focus();
        Closed += (_, _) => { _lifetime.Cancel(); PasswordInput.Clear(); };
    }
    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true; LoginButton.IsEnabled = false; ErrorText.Text = "";
        LoginBox.IsEnabled = PasswordInput.IsEnabled = CodeBox.IsEnabled = false;
        try
        {
            await _accounts.LoginAsync(LoginBox.Text, PasswordInput.Password, CodePanel.IsVisible ? CodeBox.Text : null, _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) DialogResult = true;
        }
        catch (ElyTwoFactorRequiredException)
        {
            CodePanel.Visibility = Visibility.Visible; Height = 535;
            ErrorText.Text = LocalizationService.IsEnglish ? "Enter the six-digit code from your authenticator." : "Введите шестизначный код из приложения аутентификации.";
            _ = Dispatcher.BeginInvoke(new Action(() => CodeBox.Focus()));
        }
        catch (OperationCanceledException) { if (!_lifetime.IsCancellationRequested) ErrorText.Text = Text("Время ожидания истекло. Попробуйте ещё раз.", "The request timed out. Please try again."); }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested) ErrorText.Text = ex is InvalidOperationException ? LocalizationService.Translate(ex.Message) : Text("Не удалось подключиться к Ely.by. Повторите попытку.", "Couldn't connect to Ely.by. Please try again.");
        }
        finally
        {
            _busy = false; LoginButton.IsEnabled = true;
            LoginBox.IsEnabled = PasswordInput.IsEnabled = CodeBox.IsEnabled = true;
        }
    }
    private static string Text(string ru, string en) => LocalizationService.IsEnglish ? en : ru;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Register_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://account.ely.by/register") { UseShellExecute = true }); }
        catch { ErrorText.Text = Text("Не удалось открыть браузер.", "Couldn't open the browser."); }
    }
}
