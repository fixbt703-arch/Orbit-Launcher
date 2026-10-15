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

	private MSession? _microsoftSession;

	private JELoginHandler? _loginHandler;

	private string _activeClientId = string.Empty;

	public string DisplayName { get; private set; } = "Без аккаунта";

	public bool IsSignedIn => _microsoftSession != null;

	public AuthService(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}

	public async Task<MSession?> TryRestoreSessionAsync(LauncherSettings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
		{
			return null;
		}
		try
		{
			MSession mSession = await (await GetLoginHandlerAsync(settings.MicrosoftClientId)).AuthenticateSilently();
			SetSession(mSession);
			return mSession;
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
		MSession mSession = await (await GetLoginHandlerAsync(settings.MicrosoftClientId)).AuthenticateInteractively();
		SetSession(mSession);
		return mSession;
	}

	public async Task<MSession> GetRequiredSessionAsync(LauncherSettings settings)
	{
		if (_microsoftSession != null)
		{
			return _microsoftSession;
		}
		return (await TryRestoreSessionAsync(settings)) ?? throw new InvalidOperationException("Сначала войдите в лицензионный Microsoft-аккаунт Minecraft.");
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
		_ = 1;
		try
		{
			if (!string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
			{
				await (await GetLoginHandlerAsync(settings.MicrosoftClientId)).Signout();
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
