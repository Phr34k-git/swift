using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Client.Services;

/// <summary>
/// Authenticated client for /api/v1/swift/account endpoints. Uses the in-memory access
/// token from <see cref="AppStateService"/> and routes auth failures back through it.
/// </summary>
public sealed class AccountApiClient
{
    private readonly AppStateService _appState;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates an account API client bound to the supplied app state service.
    /// </summary>
    public AccountApiClient(AppStateService appState, HttpClient? httpClient = null)
    {
        _appState = appState;
        _httpClient = httpClient ?? ApiHttp.SharedClient;
    }

    /// <summary>
    /// Fetches the current account snapshot.
    /// </summary>
    public async Task<AccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Get, "/api/v1/swift/account");
        return await SendAsync(request, "GetAccount", cancellationToken);
    }

    /// <summary>
    /// Updates the app username for the signed-in account.
    /// </summary>
    public async Task<AccountSnapshot> UpdateUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(
            HttpMethod.Put,
            "/api/v1/swift/account/username",
            new UsernameUpdateBody(username),
            ApiJsonContext.Default.UsernameUpdateBody);
        return await SendAsync(request, "UpdateUsername", cancellationToken);
    }

    /// <summary>
    /// Sets or changes the account password.
    /// </summary>
    public async Task<AccountSnapshot> UpdatePasswordAsync(
        string? currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var body = new PasswordUpdateBody(
            string.IsNullOrEmpty(currentPassword) ? null : currentPassword,
            newPassword);
        using var request = BuildRequest(
            HttpMethod.Put,
            "/api/v1/swift/account/password",
            body,
            ApiJsonContext.Default.PasswordUpdateBody);
        return await SendAsync(request, "UpdatePassword", cancellationToken);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var token = _appState.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw AccountApiException.Unauthenticated();
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return request;
    }

    private HttpRequestMessage BuildRequest<TBody>(
        HttpMethod method,
        string path,
        TBody body,
        JsonTypeInfo<TBody> jsonTypeInfo)
    {
        var request = BuildRequest(method, path);
        request.Content = ApiJson.CreateContent(body, jsonTypeInfo);
        return request;
    }

    private async Task<AccountSnapshot> SendAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            AppLog.Info("AccountApiClient", $"{operation} request started.");
            response = await _httpClient.SendAsync(request, cancellationToken);
            AppLog.Info("AccountApiClient", $"{operation} response HTTP {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException ex)
        {
            AppLog.Error("AccountApiClient", $"{operation} timed out.", ex);
            throw AccountApiException.Transient($"{operation} timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("AccountApiClient", $"{operation} connection failed.", ex);
            throw AccountApiException.Transient("Could not reach the server.", ex);
        }

        try
        {
            if (response.IsSuccessStatusCode)
            {
                var snapshot = await ApiJson.ReadAsync(
                    response.Content,
                    ApiJsonContext.Default.AccountSnapshot,
                    cancellationToken);
                if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.DiscordId))
                {
                    throw AccountApiException.Transient(
                        $"{operation} response was empty.",
                        inner: null);
                }
                return snapshot;
            }

            var (reason, detail) = await ApiHttp.ReadErrorAsync(response);
            AppLog.Info("AccountApiClient", $"{operation} error parsed. reason={reason ?? "none"}, detail={detail ?? "none"}.");
            throw BuildError(response, reason, detail);
        }
        catch (AccountApiException ex)
        {
            AppLog.Error("AccountApiClient", $"{operation} API failure. kind={ex.Kind}, status={(int?)ex.StatusCode}, reason={ex.Reason ?? "none"}.", ex);
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("AccountApiClient", $"{operation} failed unexpectedly.", ex);
            throw;
        }
        finally
        {
            response.Dispose();
        }
    }

    private AccountApiException BuildError(
        HttpResponseMessage response,
        string? reason,
        string? detail)
    {
        var status = response.StatusCode;

        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
        {
            var authMessage = string.IsNullOrWhiteSpace(detail)
                ? $"Authentication failed ({(int)status})."
                : detail;
            _appState.HandleApiAuthFailure(new AuthApiException(status, reason, authMessage));
            return AccountApiException.AuthFailureHandled();
        }

        if (status == HttpStatusCode.Conflict)
        {
            return new AccountApiException(
                AccountApiErrorKind.Conflict,
                status,
                reason,
                detail ?? "Conflict.");
        }

        if (status == HttpStatusCode.UnprocessableEntity)
        {
            return new AccountApiException(
                AccountApiErrorKind.ValidationError,
                status,
                reason,
                detail ?? "Validation failed.");
        }

        if (status == HttpStatusCode.BadRequest)
        {
            return new AccountApiException(
                AccountApiErrorKind.InvalidRequest,
                status,
                reason,
                detail ?? "Invalid request.");
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            int? retryAfter = null;
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var value in values)
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                    {
                        retryAfter = seconds;
                        break;
                    }
                }
            }
            return AccountApiException.RateLimited(retryAfter, detail);
        }

        return AccountApiException.Transient(
            string.IsNullOrWhiteSpace(detail)
                ? $"Request failed ({(int)status})."
                : detail,
            inner: null);
    }

}

/// <summary>
/// Snapshot of the signed-in account as returned by /api/v1/swift/account.
/// </summary>
public sealed class AccountSnapshot
{
    [JsonPropertyName("discord_id")]
    public string DiscordId { get; init; } = string.Empty;

    [JsonPropertyName("discord_username")]
    public string? DiscordUsername { get; init; }

    [JsonPropertyName("discord_display_name")]
    public string? DiscordDisplayName { get; init; }

    [JsonPropertyName("discord_avatar_url")]
    public string? DiscordAvatarUrl { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("has_password")]
    public bool HasPassword { get; init; }

    [JsonPropertyName("is_licensed")]
    public bool IsLicensed { get; init; }
}

/// <summary>
/// Categorizes failures from <see cref="AccountApiClient"/> so view models can
/// render appropriate inline feedback or defer to centralized auth handling.
/// </summary>
public enum AccountApiErrorKind
{
    Unauthenticated,
    AuthFailureHandled,
    InvalidRequest,
    Conflict,
    ValidationError,
    RateLimited,
    Transient,
}

/// <summary>
/// Exception type for <see cref="AccountApiClient"/> failures.
/// </summary>
public sealed class AccountApiException : Exception
{
    public AccountApiException(
        AccountApiErrorKind kind,
        HttpStatusCode? statusCode,
        string? reason,
        string message,
        int? retryAfterSeconds = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        Reason = reason;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public AccountApiErrorKind Kind { get; }

    public HttpStatusCode? StatusCode { get; }

    public string? Reason { get; }

    public int? RetryAfterSeconds { get; }

    public static AccountApiException Unauthenticated()
    {
        return new AccountApiException(
            AccountApiErrorKind.Unauthenticated,
            null,
            null,
            "Not signed in.");
    }

    public static AccountApiException AuthFailureHandled()
    {
        return new AccountApiException(
            AccountApiErrorKind.AuthFailureHandled,
            null,
            null,
            "Authentication failure handled by app state.");
    }

    public static AccountApiException RateLimited(int? retryAfterSeconds, string? detail)
    {
        return new AccountApiException(
            AccountApiErrorKind.RateLimited,
            HttpStatusCode.TooManyRequests,
            null,
            detail ?? "Too many requests.",
            retryAfterSeconds);
    }

    public static AccountApiException Transient(string message, Exception? inner)
    {
        return new AccountApiException(
            AccountApiErrorKind.Transient,
            null,
            null,
            message,
            inner: inner);
    }
}
