using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Client.Tests.Fakes;
using Xunit;

namespace Client.Tests;

public sealed class AccountApiClientTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static AuthTokens MakeTokens() => new(
        "test-access-token",
        DateTimeOffset.UtcNow.AddMinutes(2),
        "test-refresh-token",
        DateTimeOffset.UtcNow.AddDays(90));

    private static async Task<AppStateService> CreateRunningServiceAsync()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("stored-token");
        auth.EnqueueRefresh(MakeTokens());
        var svc = new AppStateService(auth, new FakeHwidProvider(), new FakeOffsetsRuntime());
        await svc.InitializeAsync();
        return svc;
    }

    private static AccountApiClient BuildClient(AppStateService appState, FakeHttpMessageHandler handler) =>
        new(appState, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccount_NoAccessToken_ThrowsUnauthenticated()
    {
        // AppStateService freshly created — AccessToken is null
        var appState = new AppStateService(new FakeAuthService(), new FakeHwidProvider());
        var handler = new FakeHttpMessageHandler();
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.Unauthenticated, ex.Kind);
    }

    [Fact]
    public async Task GetAccount_Success_ReturnsSnapshot()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK,
            """{"discord_id":"12345","discord_username":"user#0001","is_licensed":true}"""));
        var client = BuildClient(appState, handler);

        var snapshot = await client.GetAccountAsync();

        Assert.Equal("12345", snapshot.DiscordId);
        Assert.True(snapshot.IsLicensed);
        Assert.Equal("/api/v1/swift/account", Assert.Single(handler.RequestPaths));
    }

    [Fact]
    public async Task GetAccount_401_ThrowsAuthFailureHandled_AndTransitionsState()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.Unauthorized,
            """{"detail":"token expired","reason":"expired"}"""));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.AuthFailureHandled, ex.Kind);
        // HandleApiAuthFailure with non-transient 401 → stops loop → Login
        Assert.Equal(AppState.Login, appState.CurrentState);
    }

    [Fact]
    public async Task GetAccount_403_ThrowsAuthFailureHandled()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.Forbidden,
            """{"detail":"forbidden","reason":"unlicensed"}"""));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.AuthFailureHandled, ex.Kind);
    }

    [Fact]
    public async Task UpdateUsername_409_ThrowsConflict()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.Conflict,
            """{"detail":"username taken","reason":"username_taken"}"""));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(
            () => client.UpdateUsernameAsync("taken"));

        Assert.Equal(AccountApiErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public async Task UpdateUsername_422_ThrowsValidationError()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.UnprocessableEntity,
            """{"detail":"invalid username"}"""));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(
            () => client.UpdateUsernameAsync("!!bad!!"));

        Assert.Equal(AccountApiErrorKind.ValidationError, ex.Kind);
    }

    [Fact]
    public async Task GetAccount_429_WithRetryAfterHeader_ThrowsRateLimitedWithSeconds()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        var response = JsonResponse((HttpStatusCode)429, """{"detail":"slow down"}""");
        response.Headers.Add("Retry-After", "60");
        handler.Enqueue(response);
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.RateLimited, ex.Kind);
        Assert.Equal(60, ex.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetAccount_429_WithoutRetryAfterHeader_ThrowsRateLimitedNullSeconds()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse((HttpStatusCode)429, """{"detail":"slow down"}"""));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.RateLimited, ex.Kind);
        Assert.Null(ex.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetAccount_500_ThrowsTransient()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.InternalServerError,
            """{"detail":"server error"}"""));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task GetAccount_NetworkException_ThrowsTransient()
    {
        var appState = await CreateRunningServiceAsync();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("network failure"));
        var client = BuildClient(appState, handler);

        var ex = await Assert.ThrowsAsync<AccountApiException>(() => client.GetAccountAsync());

        Assert.Equal(AccountApiErrorKind.Transient, ex.Kind);
    }
}
