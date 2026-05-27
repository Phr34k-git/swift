using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Credentials;

namespace Client.Services;

public sealed class AuthService : IAuthService
{
    private const string VaultResource = "OpenMacro";
    private const string VaultUsername = "RefreshToken";
    private const string LegacyVaultResource = "FischGPT";
    private const string LegacyVaultUsername = "JWT";

    private readonly HttpClient _httpClient;

    public AuthService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? ApiHttp.SharedClient;
    }

    /// <summary>
    /// Stores the refresh token in Windows PasswordVault, replacing any existing entry.
    /// </summary>
    public void StoreRefreshToken(string refreshToken)
    {
        var vault = new PasswordVault();
        var existingCredential = TryRetrieveCredential(vault, VaultResource, VaultUsername);

        if (existingCredential is not null)
        {
            vault.Remove(existingCredential);
        }

        vault.Add(new PasswordCredential(VaultResource, VaultUsername, refreshToken));
    }

    /// <summary>
    /// Loads the refresh token from Windows PasswordVault, returning null when missing.
    /// </summary>
    public string? LoadRefreshToken()
    {
        try
        {
            var vault = new PasswordVault();
            var credential = TryRetrieveCredential(vault, VaultResource, VaultUsername);

            if (credential is null)
            {
                return null;
            }

            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the refresh token from Windows PasswordVault when present.
    /// </summary>
    public void DeleteRefreshToken()
    {
        DeleteCredential(VaultResource, VaultUsername);
    }

    /// <summary>
    /// Deletes the legacy stored JWT from the unreleased previous auth flow.
    /// </summary>
    public void DeleteLegacyJwt()
    {
        DeleteCredential(LegacyVaultResource, LegacyVaultUsername);
    }

    /// <summary>
    /// Gets the Discord OAuth start URL.
    /// </summary>
    public string GetAuthUrl()
    {
        var url = $"{ApiHttp.BaseUrl}/api/v1/swift/auth/discord";
        AppLog.Info("AuthService", "Built Discord auth URL.");
        return url;
    }

    /// <summary>
    /// Exchanges a local callback code and HWID hash for an access/refresh token pair.
    /// </summary>
    public Task<AuthTokens> ExchangeCodeAsync(string code, string hwidHash)
    {
        return SendTokenRequestAsync(
            "/api/v1/swift/auth/exchange",
            new ExchangeCodeRequest(code, hwidHash),
            ApiJsonContext.Default.ExchangeCodeRequest,
            "Exchange");
    }

    /// <summary>
    /// Signs in with an existing app username/password and returns a token pair.
    /// </summary>
    public Task<AuthTokens> LoginAsync(string username, string password, string hwidHash)
    {
        return SendTokenRequestAsync(
            "/api/v1/swift/auth/login",
            new CredentialsLoginRequest(username, password, hwidHash),
            ApiJsonContext.Default.CredentialsLoginRequest,
            "Login");
    }

    /// <summary>
    /// Rotates a refresh token and returns a fresh access/refresh token pair.
    /// </summary>
    public Task<AuthTokens> RefreshAsync(string refreshToken, string hwidHash)
    {
        return SendTokenRequestAsync(
            "/api/v1/swift/auth/refresh",
            new RefreshRequest(refreshToken, hwidHash),
            ApiJsonContext.Default.RefreshRequest,
            "Refresh");
    }

    /// <summary>
    /// Calls the read-only session heartbeat endpoint. Throws on any non-200 response.
    /// </summary>
    public async Task<SessionStatusInfo> GetSessionStatusAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            AppLog.Info("AuthService", "SessionStatus request started.");
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/swift/session/status");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            AppLog.Info("AuthService", $"SessionStatus response HTTP {(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateAuthApiExceptionAsync(response, "SessionStatus");
            }

            var body = await ApiJson.ReadAsync(
                response.Content,
                ApiJsonContext.Default.SessionStatusResponse,
                cancellationToken);

            return new SessionStatusInfo(body?.OffsetsVersion);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Error("AuthService", "SessionStatus timed out.", ex);
            throw AuthApiException.Transient("Session status timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("AuthService", "SessionStatus connection failed.", ex);
            throw AuthApiException.Transient("Could not connect to the API.", ex);
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("AuthService", $"SessionStatus API failure. Status={(int?)ex.StatusCode}, reason={ex.Reason ?? "none"}.", ex);
            throw;
        }
    }

    /// <summary>
    /// Revokes the server-side refresh token family for logout.
    /// </summary>
    public async Task LogoutAsync(string refreshToken)
    {
        try
        {
            AppLog.Info("AuthService", "Logout request started.");
            using var content = ApiJson.CreateContent(
                new LogoutRequest(refreshToken),
                ApiJsonContext.Default.LogoutRequest);
            using var response = await _httpClient.PostAsync("/api/v1/swift/auth/logout", content, CancellationToken.None);
            AppLog.Info("AuthService", $"Logout response HTTP {(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateAuthApiExceptionAsync(response, "Logout");
            }
        }
        catch (TaskCanceledException ex)
        {
            AppLog.Error("AuthService", "Logout timed out.", ex);
            throw AuthApiException.Transient("Logout timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("AuthService", "Logout connection failed.", ex);
            throw AuthApiException.Transient("Could not connect to the API.", ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("AuthService", "Logout failed unexpectedly.", ex);
            throw;
        }
    }

    private async Task<AuthTokens> SendTokenRequestAsync<TRequest>(
        string path,
        TRequest requestBody,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestJsonType,
        string operation)
    {
        try
        {
            AppLog.Info("AuthService", $"{operation} request started for {path}.");
            using var content = ApiJson.CreateContent(requestBody, requestJsonType);
            using var response = await _httpClient.PostAsync(path, content, CancellationToken.None);
            AppLog.Info("AuthService", $"{operation} response HTTP {(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateAuthApiExceptionAsync(response, operation);
            }

            var body = await ApiJson.ReadAsync(
                response.Content,
                ApiJsonContext.Default.TokenPairResponse,
                CancellationToken.None);
            if (body is null ||
                string.IsNullOrWhiteSpace(body.AccessToken) ||
                string.IsNullOrWhiteSpace(body.RefreshToken))
            {
                throw new AuthApiException(
                    response.StatusCode,
                    null,
                    $"{operation} response did not include a complete token pair.");
            }

            return new AuthTokens(
                body.AccessToken,
                body.AccessTokenExpiresAt,
                body.RefreshToken,
                body.RefreshTokenExpiresAt);
        }
        catch (TaskCanceledException ex)
        {
            AppLog.Error("AuthService", $"{operation} timed out.", ex);
            throw AuthApiException.Transient($"{operation} timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("AuthService", $"{operation} connection failed.", ex);
            throw AuthApiException.Transient("Could not connect to the API.", ex);
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("AuthService", $"{operation} API failure. Status={(int?)ex.StatusCode}, reason={ex.Reason ?? "none"}.", ex);
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("AuthService", $"{operation} failed unexpectedly.", ex);
            throw;
        }
    }

    private static async Task<AuthApiException> CreateAuthApiExceptionAsync(
        HttpResponseMessage response,
        string operation)
    {
        var (reason, detail) = await ApiHttp.ReadErrorAsync(response);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{operation} failed with status code {(int)response.StatusCode} ({response.StatusCode})."
            : detail;

        AppLog.Info("AuthService", $"{operation} API error parsed. reason={reason ?? "none"}, detail={message}");
        return new AuthApiException(response.StatusCode, reason, message);
    }

    private static void DeleteCredential(string resource, string username)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = TryRetrieveCredential(vault, resource, username);

            if (credential is not null)
            {
                vault.Remove(credential);
            }
        }
        catch
        {
        }
    }

    private static PasswordCredential? TryRetrieveCredential(
        PasswordVault vault,
        string resource,
        string username)
    {
        try
        {
            return vault.Retrieve(resource, username);
        }
        catch
        {
            return null;
        }
    }

}

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record SessionStatusInfo(string? OffsetsVersion);

public sealed class AuthApiException : Exception
{
    public AuthApiException(HttpStatusCode? statusCode, string? reason, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Reason = reason;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? Reason { get; }

    public bool IsTransient =>
        StatusCode is null ||
        StatusCode == HttpStatusCode.TooManyRequests ||
        (int)StatusCode >= 500 ||
        (StatusCode == HttpStatusCode.Forbidden && Reason is null);

    public static AuthApiException Transient(string message, Exception inner)
    {
        return new AuthApiException(null, null, message, inner);
    }
}
