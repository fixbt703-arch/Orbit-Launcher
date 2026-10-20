using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OrbitLauncher.Utilities;

namespace OrbitLauncher;

public partial class MainWindow
{
    private async Task UpdateElyAccountCardAsync(int revision)
    {
        var account = _elyAccounts.Account;
        var name = account?.Name ?? _settings.OfflinePlayerName;
        AccountNameText.Text = SettingsAccountNameText.Text = name;
        AccountModeText.Text = LocalizationService.Translate(account is null ? "Аккаунт без лицензии" : "Ely.by • Minecraft");
        SettingsAccountHintText.Text = LocalizationService.Translate(account is null ? "Аккаунт без лицензии" : "Аккаунт без лицензии · скины Ely.by");
        AccountStatusDot.Fill = new SolidColorBrush(Color.FromRgb(61, 220, 164));
        SettingsLoginButton.Visibility = SignOutButton.Visibility = GateLoginButton.Visibility = Visibility.Collapsed;
        UpdateSkinAccountState();
        var owner = account is null ? null : "ely:" + account.Id;
        if (_accountHeadOwner != owner) { _accountHeadOwner = owner; ShowAccountHead(null); }
        if (account is null) return;
        if (_elySkins.GetCachedHead(name) is { } cached) { ShowAccountHead(cached); return; }
        try
        {
            var head = await _elySkins.RefreshHeadAsync(name);
            if (revision == _accountHeadRevision && _settings.OfflineModeEnabled && _elyAccounts.Account?.Id == account.Id)
                ShowAccountHead(head);
        }
        catch { } // Keep the account usable during a skins-system outage.
    }

    private void ElySettings_Click(object sender, RoutedEventArgs e) => SkinsNav.IsChecked = true;
    private void EnableUnlicensedAccount_Click(object sender, RoutedEventArgs e) => OfflineModeCheck.IsChecked = true;

    private async void LicensedElySkins_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = LicensedElySkinsCheck.IsChecked == true;
        if (!IsLoaded || _populatingSettings || _settings.ShowElySkinsForLicensedAccounts == enabled) return;
        _settings.ShowElySkinsForLicensedAccounts = enabled;
        try { await _settingsService.SaveAsync(_settings); }
        catch (Exception ex) { ShowError("Не удалось сохранить настройку скинов", ex); return; }
        ShowStatus(LocalizationService.IsEnglish ? "The skin setting will apply the next time Minecraft starts." : "Настройка скинов применится при следующем запуске Minecraft.");
    }
}
