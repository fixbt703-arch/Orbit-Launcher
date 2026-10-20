using System;
using System.Net.Http;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Identity.Client;
using OrbitLauncher.Models;
using OrbitLauncher.Utilities;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

namespace OrbitLauncher.Services;

public sealed class AuthService
{
	private readonly HttpClient _httpClient;
	private readonly Func<Task<MSession>>? _silentAuthentication;
	private readonly SemaphoreSlim _refreshGate = new(1, 1);

	private MSession? _microsoftSession;

	private JELoginHandler? _loginHandler;

	private string _activeClientId = string.Empty;

	public string DisplayName { get; private set; } = "Без аккаунта";

	public bool IsSignedIn => _microsoftSession != null;

	public AuthService(HttpClient httpClient, Func<Task<MSession>>? silentAuthentication = null)
	{
		_httpClient = httpClient;
		_silentAuthentication = silentAuthentication;
	}

	public async Task<MSession?> TryRestoreSessionAsync(LauncherSettings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
		{
			return null;
		}
		try
		{
			return await GetRequiredSessionAsync(settings);
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
		{
			throw new InvalidOperationException("Укажите Microsoft Client ID приложения Orbit Launcher.");
		}
        await _refreshGate.WaitAsync();
        try
        {
            var session = await (await GetLoginHandlerAsync(settings.MicrosoftClientId)).AuthenticateInteractively();
            SetSession(session); return session;
        }
        finally { _refreshGate.Release(); }
	}

	public async Task<MSession> GetRequiredSessionAsync(LauncherSettings settings)
	{
		await _refreshGate.WaitAsync();
		try
		{
			if (_silentAuthentication is null && string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
				throw new InvalidOperationException("Сначала настройте вход в Microsoft-аккаунт Minecraft.");
			// The library reuses valid tokens and refreshes expired credentials without opening a browser.
			var session = _silentAuthentication is not null ? await _silentAuthentication()
				: await (await GetLoginHandlerAsync(settings.MicrosoftClientId)).AuthenticateSilently();
			SetSession(session);
			return session;
		}
		finally { _refreshGate.Release(); }
	}

	public async Task<string> GetRequiredAccessTokenAsync(LauncherSettings settings)
	{
		MSession obj = await GetRequiredSessionAsync(settings);
		if (string.IsNullOrWhiteSpace(obj.AccessToken))
		{
			throw new InvalidOperationException("Microsoft не вернул токен Minecraft Services.");
		}
		return obj.AccessToken;
	}

	public async Task SignOutAsync(LauncherSettings settings)
	{
        await _refreshGate.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
			{
				await (await GetLoginHandlerAsync(settings.MicrosoftClientId)).Signout();
			}
		}
		finally
		{
            ClearSession(); _refreshGate.Release();
        }
    }

	private async Task<JELoginHandler> GetLoginHandlerAsync(string clientId)
	{
		clientId = clientId.Trim();
		if (_loginHandler != null && string.Equals(_activeClientId, clientId, StringComparison.OrdinalIgnoreCase))
		{
			return _loginHandler;
		}
		IPublicClientApplication app = await MsalClientHelper.BuildApplicationWithCache(clientId);
		_loginHandler = new JELoginHandlerBuilder().WithHttpClient(_httpClient).WithAccountManager(AppPaths.AccountsFile).WithOAuthProvider(new MsalCodeFlowProvider(app))
			.Build();
		_activeClientId = clientId;
		return _loginHandler;
	}

	private void SetSession(MSession session)
	{
		_microsoftSession = session ?? throw new InvalidOperationException("Microsoft не вернул игровую сессию.");
		DisplayName = (string.IsNullOrWhiteSpace(session.Username) ? "Minecraft Player" : session.Username);
	}

	private void ClearSession()
	{
		_microsoftSession = null;
		DisplayName = "Без аккаунта";
	}
}
