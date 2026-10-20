using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Services;

public sealed record ElyAccount(string Name, string Id, string AccessToken, string ClientToken);
public sealed class ElyTwoFactorRequiredException : Exception
{
    public ElyTwoFactorRequiredException() : base("Введите код двухфакторной аутентификации Ely.by.") { }
}

/// <summary>Official Yggdrasil API. Passwords are used once and never written to disk.</summary>
public sealed class ElyAccountService
{
    private readonly HttpClient _http;
    private readonly string _sessionFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OrbitLauncher.Ely.by.session.v1");
    public ElyAccount? Account { get; private set; }
    public bool IsSignedIn => Account is not null;

    public ElyAccountService(HttpClient http, string? sessionFile = null)
    {
        _http = http;
        _sessionFile = sessionFile ?? Path.Combine(AppPaths.Root, "ely-session.dat");
    }

    public async Task RestoreAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_sessionFile)) return;
            var protectedBytes = await File.ReadAllBytesAsync(_sessionFile);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try { Account = Validate(JsonSerializer.Deserialize<ElyAccount>(bytes)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
        { Account = null; } // Keep a damaged file for recovery; never disturb the Microsoft account.
        finally { _gate.Release(); }
    }

    public async Task LoginAsync(string login, string password, string? code = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Укажите логин и пароль Ely.by.");
        if (!string.IsNullOrWhiteSpace(code) && !Regex.IsMatch(code.Trim(), "^[0-9]{6}$"))
            throw new InvalidOperationException("Код Ely.by должен содержать 6 цифр.");
        await _gate.WaitAsync(token);
        try
        {
            var clientToken = Guid.NewGuid().ToString("N");
            using var result = await PostAsync("authenticate", new
            {
                username = login.Trim(), password = string.IsNullOrWhiteSpace(code) ? password : password + ":" + code.Trim(),
                clientToken, requestUser = true
            }, token);
            var account = Parse(result.RootElement, clientToken);
            await SaveAsync(account, token);
            Account = account;
        }
        finally { _gate.Release(); }
    }

    public async Task<MSession> GetRequiredSessionAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var previous = Account ?? throw new InvalidOperationException("Войдите в Ely.by во вкладке «Скины».");
            JsonDocument result;
            try
            {
                result = await PostAsync("refresh", new { accessToken = previous.AccessToken, clientToken = previous.ClientToken, requestUser = true }, token);
            }
            catch (ElySessionExpiredException)
            {
                Account = null;
                if (File.Exists(_sessionFile)) File.Delete(_sessionFile);
                throw;
            }
            using (result)
            {
                var refreshed = Parse(result.RootElement, previous.ClientToken);
                // Never accept another profile silently when refreshing an existing login.
                if (!refreshed.Id.Equals(previous.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Ely.by вернул другой игровой профиль. Войдите заново.");
                await SaveAsync(refreshed, token);
                Account = refreshed;
                return new MSession { Username = refreshed.Name, UUID = refreshed.Id, AccessToken = refreshed.AccessToken, ClientToken = refreshed.ClientToken, UserType = "mojang" };
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SignOutAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var account = Account;
            if (File.Exists(_sessionFile)) File.Delete(_sessionFile);
            Account = null;
            if (account is null) return;
            try
            {
                using var result = await PostAsync("invalidate", new { accessToken = account.AccessToken, clientToken = account.ClientToken }, token);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or JsonException) { }
        }
        finally { _gate.Release(); }
    }

    private async Task<JsonDocument> PostAsync(string operation, object payload, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        // The shared transport does not replay POSTs. Credentials never enter logs or URLs.
        using var response = await _http.PostAsJsonAsync("https://authserver.ely.by/auth/" + operation, payload, deadline.Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
        if (bytes.Length > 131072) throw new InvalidDataException("Некорректный ответ Ely.by.");
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                using var error = JsonDocument.Parse(bytes);
                string message = error.RootElement.TryGetProperty("errorMessage", out var text) ? text.GetString() ?? "" : "";
                if (message == "Account protected with two factor auth.") throw new ElyTwoFactorRequiredException();
                if (operation == "refresh") throw new ElySessionExpiredException();
                throw new InvalidOperationException("Не удалось войти в Ely.by. Проверьте логин, пароль и код подтверждения.");
            }
            if ((int)response.StatusCode == 429) throw new InvalidOperationException("Слишком много попыток входа. Подождите немного и повторите.");
            throw new HttpRequestException("Ely.by временно недоступен. Попробуйте ещё раз.", null, response.StatusCode);
        }
        return JsonDocument.Parse(bytes.Length == 0 ? "{}"u8.ToArray() : bytes);
    }

    private static ElyAccount Parse(JsonElement root, string clientToken)
    {
        var profile = root.GetProperty("selectedProfile");
        return Validate(new ElyAccount(profile.GetProperty("name").GetString()!, profile.GetProperty("id").GetString()!,
            root.GetProperty("accessToken").GetString()!, root.TryGetProperty("clientToken", out var client) ? client.GetString()! : clientToken));
    }

    private static ElyAccount Validate(ElyAccount? account)
    {
        if (account is null || !Regex.IsMatch(account.Name ?? "", "^[A-Za-z0-9_]{3,16}$") ||
            !Guid.TryParse(account.Id, out var id) || string.IsNullOrWhiteSpace(account.AccessToken) || string.IsNullOrWhiteSpace(account.ClientToken))
            throw new InvalidDataException("Ely.by не вернул корректный игровой профиль.");
        return account with { Id = id.ToString("N") };
    }

    private async Task SaveAsync(ElyAccount account, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_sessionFile))!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(account);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var temporary = _sessionFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, encrypted, token); File.Move(temporary, _sessionFile, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class ElySessionExpiredException : InvalidOperationException
{
    public ElySessionExpiredException() : base("Сессия Ely.by истекла. Войдите заново во вкладке «Скины».") { }
}
