using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

namespace OrbitLauncher.Services;

public sealed class AuthService
{
    private readonly HttpClient _httpClient;
    private MSession? _microsoftSession;
    private JELoginHandler? _loginHandler;
    private string _activeClientId = string.Empty;

    public AuthService(HttpClient httpClient) => _httpClient = httpClient;

    public string DisplayName { get; private set; } = "Без аккаунта";
    public bool IsSignedIn => _microsoftSession is not null;

    public async Task<MSession?> TryRestoreSessionAsync(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
            return null;

        try
        {
            var handler = await GetLoginHandlerAsync(settings.MicrosoftClientId);
            var session = await handler.AuthenticateSilently();
            SetSession(session);
            return session;
        }
        catch
        {
            ClearSession();
            return null;
        }
    }

    public async Task<MSession> LoginInteractivelyAsync(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
            throw new InvalidOperationException("Укажите Microsoft Client ID приложения Orbit Launcher.");

        var handler = await GetLoginHandlerAsync(settings.MicrosoftClientId);
        var session = await handler.AuthenticateInteractively();
        SetSession(session);
        return session;
    }

    public async Task<MSession> GetRequiredSessionAsync(LauncherSettings settings)
    {
        if (_microsoftSession is not null)
            return _microsoftSession;

        var restored = await TryRestoreSessionAsync(settings);
        return restored ?? throw new InvalidOperationException("Сначала войдите в лицензионный Microsoft-аккаунт Minecraft.");
    }

    public async Task SignOutAsync(LauncherSettings settings)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
            {
                var handler = await GetLoginHandlerAsync(settings.MicrosoftClientId);
                await handler.Signout();
            }
        }
        finally
        {
            ClearSession();
        }
    }

    private async Task<JELoginHandler> GetLoginHandlerAsync(string clientId)
    {
        clientId = clientId.Trim();
        if (_loginHandler is not null && string.Equals(_activeClientId, clientId, StringComparison.OrdinalIgnoreCase))
            return _loginHandler;

        var app = await MsalClientHelper.BuildApplicationWithCache(clientId);
        _loginHandler = new JELoginHandlerBuilder()
            .WithHttpClient(_httpClient)
            .WithAccountManager(AppPaths.AccountsFile)
            .WithOAuthProvider(new MsalCodeFlowProvider(app))
            .Build();
        _activeClientId = clientId;
        return _loginHandler;
    }

    private void SetSession(MSession session)
    {
        _microsoftSession = session ?? throw new InvalidOperationException("Microsoft не вернул игровую сессию.");
        DisplayName = string.IsNullOrWhiteSpace(session.Username) ? "Minecraft Player" : session.Username;
    }

    private void ClearSession()
    {
        _microsoftSession = null;
        DisplayName = "Без аккаунта";
    }
}
