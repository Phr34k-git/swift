using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Services;

namespace Client.Tests.Fakes;

internal sealed class FakeAuthService : IAuthService
{
    private string? _storedToken;
    private readonly Queue<Func<Task<AuthTokens>>> _refreshQueue = new();
    private readonly Queue<Func<Task<AuthTokens>>> _loginQueue = new();
    private readonly Queue<Func<Task<AuthTokens>>> _exchangeQueue = new();
    private readonly Queue<Func<Task<SessionStatusInfo>>> _sessionStatusQueue = new();

    public int DeleteRefreshTokenCallCount { get; private set; }
    public int StoreRefreshTokenCallCount { get; private set; }
    public int RefreshCallCount { get; private set; }
    public int SessionStatusCallCount { get; private set; }

    public void SetStoredRefreshToken(string? token) => _storedToken = token;

    public void EnqueueRefresh(AuthTokens tokens) =>
        _refreshQueue.Enqueue(() => Task.FromResult(tokens));

    public void EnqueueRefreshError(AuthApiException ex) =>
        _refreshQueue.Enqueue(() => Task.FromException<AuthTokens>(ex));

    public void EnqueueLogin(AuthTokens tokens) =>
        _loginQueue.Enqueue(() => Task.FromResult(tokens));

    public void EnqueueLoginError(AuthApiException ex) =>
        _loginQueue.Enqueue(() => Task.FromException<AuthTokens>(ex));

    public void EnqueueExchange(AuthTokens tokens) =>
        _exchangeQueue.Enqueue(() => Task.FromResult(tokens));

    public void EnqueueExchangeError(AuthApiException ex) =>
        _exchangeQueue.Enqueue(() => Task.FromException<AuthTokens>(ex));

    public void EnqueueSessionStatus(string? offsetsVersion = null) =>
        _sessionStatusQueue.Enqueue(() => Task.FromResult(new SessionStatusInfo(offsetsVersion)));

    public void EnqueueSessionStatusError(AuthApiException ex) =>
        _sessionStatusQueue.Enqueue(() => Task.FromException<SessionStatusInfo>(ex));

    // IAuthService
    public string? LoadRefreshToken() => _storedToken;
    public void StoreRefreshToken(string token) { StoreRefreshTokenCallCount++; _storedToken = token; }
    public void DeleteRefreshToken() { DeleteRefreshTokenCallCount++; _storedToken = null; }
    public void DeleteLegacyJwt() { }
    public string GetAuthUrl() => "https://example.com/api/v1/swift/auth/discord";

    public Task<AuthTokens> ExchangeCodeAsync(string code, string hwidHash) =>
        _exchangeQueue.Dequeue()();

    public Task<AuthTokens> LoginAsync(string username, string password, string hwidHash) =>
        _loginQueue.Dequeue()();

    public Task<AuthTokens> RefreshAsync(string refreshToken, string hwidHash)
    {
        RefreshCallCount++;
        return _refreshQueue.Dequeue()();
    }

    public Task LogoutAsync(string refreshToken) => Task.CompletedTask;

    public Task<SessionStatusInfo> GetSessionStatusAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        SessionStatusCallCount++;
        if (_sessionStatusQueue.Count == 0)
        {
            return Task.FromResult(new SessionStatusInfo(null));
        }

        return _sessionStatusQueue.Dequeue()();
    }
}
