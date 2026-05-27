using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Client.Models;
using Client.Services.Fishing;

namespace Client.Services;

/// <summary>
/// Coordinates authentication, session heartbeat, and app-level state transitions.
/// </summary>
public sealed class AppStateService : IDisposable
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultHeartbeatRetryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultProactiveRefreshLeadTime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultProactiveRefreshRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RestoreSessionMinimumDuration = TimeSpan.FromMilliseconds(600);

    private readonly object _syncRoot = new();
    private readonly IAuthService _authService;
    private readonly IHwidProvider _hwidProvider;
    private readonly IOffsetsRuntime _offsetsService;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _heartbeatRetryInterval;
    private readonly TimeSpan _proactiveRefreshLeadTime;
    private readonly TimeSpan _proactiveRefreshRetryInterval;

    private CancellationTokenSource? _heartbeatLoopCancellation;
    private string? _hwidHash;
    private DateTimeOffset? _accessTokenExpiresAt;
    private DateTimeOffset _nextProactiveRefreshAttemptAt = DateTimeOffset.MinValue;

    private enum RefreshAttemptResult
    {
        Succeeded,
        RetryLater,
        StopLoop
    }

    public AppStateService(
        IAuthService? authService = null,
        IHwidProvider? hwidProvider = null,
        IOffsetsRuntime? offsetsService = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatRetryInterval = null,
        TimeSpan? proactiveRefreshLeadTime = null,
        TimeSpan? proactiveRefreshRetryInterval = null)
    {
        _authService = authService ?? new AuthService();
        _hwidProvider = hwidProvider ?? new HwidServiceProvider();
        _offsetsService = offsetsService ?? new OffsetsService();
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _heartbeatRetryInterval = heartbeatRetryInterval ?? DefaultHeartbeatRetryInterval;
        _proactiveRefreshLeadTime = proactiveRefreshLeadTime ?? DefaultProactiveRefreshLeadTime;
        _proactiveRefreshRetryInterval = proactiveRefreshRetryInterval ?? DefaultProactiveRefreshRetryInterval;
        // Register as the process-wide source so RobloxMemory callsites can resolve
        // it without explicit plumbing. Heartbeat and login flows refresh it.
        OffsetsSourceProvider.Register(_offsetsService);
        CurrentState = _authService.LoadRefreshToken() is null
            ? AppState.Login
            : AppState.Initializing;
    }

    /// <summary>Gets the current app state.</summary>
    public AppState CurrentState { get; private set; } = AppState.Initializing;

    /// <summary>Gets the latest transition error message, when the current state has one.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Gets the active in-memory API access token.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>
    /// Gets the most recent lockout reason code (e.g. "banned", "chargeback", "compromised").
    /// Set when transitioning to <see cref="AppState.LockedOut"/>.
    /// </summary>
    public string? LockoutReason { get; private set; }

    /// <summary>Fires whenever the app transitions to another state.</summary>
    public event Action<AppState>? StateChanged;

    /// <summary>Fires whenever the in-memory access token is replaced or cleared.</summary>
    public event Action<string?>? AccessTokenChanged;

    /// <summary>
    /// Centralized handler for authenticated API calls that received a 401/403 response.
    /// Owns token clearing, heartbeat-loop teardown, and the resulting state transition.
    /// </summary>
    public void HandleApiAuthFailure(AuthApiException ex)
    {
        if (ex.IsTransient)
        {
            TransitionTo(AppState.Unreachable, ex.Message);
            return;
        }

        StopHeartbeatLoop();
        HandleAuthFailure(ex, clearStoredRefreshToken: true);
    }

    /// <summary>Gets the Discord OAuth start URL from the owned authentication service.</summary>
    public string GetAuthUrl()
    {
        return _authService.GetAuthUrl();
    }

    /// <summary>
    /// Initializes hardware identity, stored refresh-token state, and starts the heartbeat loop.
    /// </summary>
    public async Task InitializeAsync()
    {
        AppLog.Info("AppStateService", "InitializeAsync started.");
        StopHeartbeatLoop();
        AccessToken = null;
        _authService.DeleteLegacyJwt();

        var refreshToken = _authService.LoadRefreshToken();
        if (refreshToken is null)
        {
            AppLog.Info("AppStateService", "No stored refresh token; showing login.");
            TransitionTo(AppState.Login);
            return;
        }

        TransitionTo(AppState.Initializing);
        var restoreStartedAt = Stopwatch.GetTimestamp();

        if (!TryEnsureHwidHash(out var hwidHash))
        {
            return;
        }

        TransitionTo(AppState.Authenticating);

        try
        {
            var tokens = await _authService.RefreshAsync(refreshToken, hwidHash);
            ApplyTokenPair(tokens);
            AppLog.Info("AppStateService", "Stored refresh token accepted.");
            if (!await TryBootstrapOffsetsAsync(tokens.AccessToken, restoreStartedAt))
            {
                return;
            }
            TransitionTo(AppState.Running);
            StartHeartbeatLoop(_heartbeatInterval);
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("AppStateService", "Stored refresh token failed.", ex);
            await WaitForRestoreMinimumAsync(restoreStartedAt);
            HandleAuthFailure(ex, clearStoredRefreshToken: !ex.IsTransient);

            if (ex.IsTransient)
            {
                StartHeartbeatLoop(_heartbeatRetryInterval);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("AppStateService", "InitializeAsync failed unexpectedly.", ex);
            await WaitForRestoreMinimumAsync(restoreStartedAt);
            TransitionTo(AppState.FatalError, ex.Message);
        }
    }

    /// <summary>Completes browser OAuth by exchanging the one-time code for a token pair.</summary>
    public async Task CompleteLoginAsync(string code)
    {
        AppLog.Info("AppStateService", $"CompleteLoginAsync started. codeLength={code.Length}.");
        if (!TryEnsureHwidHash(out var hwidHash))
        {
            return;
        }

        StopHeartbeatLoop();
        TransitionTo(AppState.Authenticating);

        try
        {
            var tokens = await _authService.ExchangeCodeAsync(code, hwidHash);
            ApplyTokenPair(tokens);
            AppLog.Info("AppStateService", "Discord exchange accepted.");
            if (!await TryBootstrapOffsetsAsync(tokens.AccessToken, restoreStartedAt: null))
            {
                return;
            }
            TransitionTo(AppState.Running);
            StartHeartbeatLoop(_heartbeatInterval);
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("AppStateService", "Discord exchange failed.", ex);
            HandleAuthFailure(ex, clearStoredRefreshToken: !ex.IsTransient);

            if (ex.IsTransient)
            {
                StartHeartbeatLoop(_heartbeatRetryInterval);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("AppStateService", "CompleteLoginAsync failed unexpectedly.", ex);
            TransitionTo(AppState.FatalError, ex.Message);
        }
    }

    /// <summary>Completes username/password sign-in by exchanging credentials for a token pair.</summary>
    public async Task CompleteCredentialsLoginAsync(string username, string password)
    {
        AppLog.Info("AppStateService", $"CompleteCredentialsLoginAsync started. usernameLength={username.Length}.");
        if (!TryEnsureHwidHash(out var hwidHash))
        {
            return;
        }

        StopHeartbeatLoop();

        try
        {
            var tokens = await _authService.LoginAsync(username, password, hwidHash);
            ApplyTokenPair(tokens);
            AppLog.Info("AppStateService", "Credential login accepted.");
            if (!await TryBootstrapOffsetsAsync(tokens.AccessToken, restoreStartedAt: null))
            {
                return;
            }
            TransitionTo(AppState.Running);
            StartHeartbeatLoop(_heartbeatInterval);
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("AppStateService", "Credential login API failure.", ex);
            if (!ex.IsTransient)
            {
                HandleAuthFailure(ex, clearStoredRefreshToken: false);
            }

            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("AppStateService", "CompleteCredentialsLoginAsync failed unexpectedly.", ex);
            TransitionTo(AppState.FatalError, ex.Message);
            throw;
        }
    }

    /// <summary>Revokes the server-side refresh-token family and clears local credentials.</summary>
    public async Task LogoutAsync()
    {
        StopHeartbeatLoop();

        var refreshToken = _authService.LoadRefreshToken();
        if (refreshToken is null)
        {
            ClearRuntimeTokens();
            TransitionTo(AppState.Login);
            return;
        }

        try
        {
            await _authService.LogoutAsync(refreshToken);
            _authService.DeleteRefreshToken();
            ClearRuntimeTokens();
            TransitionTo(AppState.Login);
        }
        catch (AuthApiException ex)
        {
            TransitionTo(
                ex.IsTransient ? AppState.Unreachable : CurrentState,
                $"Logout failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            TransitionTo(AppState.FatalError, ex.Message);
        }
    }

    private void ApplyTokenPair(AuthTokens tokens)
    {
        _authService.StoreRefreshToken(tokens.RefreshToken);
        AccessToken = tokens.AccessToken;
        _accessTokenExpiresAt = tokens.AccessTokenExpiresAt;
        _nextProactiveRefreshAttemptAt = DateTimeOffset.MinValue;
        AccessTokenChanged?.Invoke(tokens.AccessToken);
    }

    private void ClearRuntimeTokens()
    {
        AccessToken = null;
        _accessTokenExpiresAt = null;
        _nextProactiveRefreshAttemptAt = DateTimeOffset.MinValue;
        // Drop cached offsets whenever runtime tokens go away. A cracked binary that
        // recovers AccessToken from memory after logout/lockout still can't read offsets.
        _offsetsService.Clear();
        AccessTokenChanged?.Invoke(null);
    }

    /// <summary>
    /// Fetch the offsets payload after a successful auth. Routes failures through
    /// the same state machine as auth failures: transient → Unreachable + retry heartbeat,
    /// permanent → HandleAuthFailure (which routes to Login/LockedOut depending on reason).
    /// Returns true if offsets are populated and the caller should proceed to Running.
    /// </summary>
    private async Task<bool> TryBootstrapOffsetsAsync(string accessToken, long? restoreStartedAt)
    {
        try
        {
            await _offsetsService.RefreshAsync(accessToken);
            if (restoreStartedAt is long started)
            {
                await WaitForRestoreMinimumAsync(started);
            }
            return true;
        }
        catch (AuthApiException ex) when (ex.IsTransient)
        {
            AppLog.Error("AppStateService", "Offsets bootstrap transient failure.", ex);
            if (restoreStartedAt is long started)
            {
                await WaitForRestoreMinimumAsync(started);
            }
            TransitionTo(AppState.Unreachable, ex.Message);
            StartHeartbeatLoop(_heartbeatRetryInterval);
            return false;
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("AppStateService", "Offsets bootstrap rejected by API.", ex);
            if (restoreStartedAt is long started)
            {
                await WaitForRestoreMinimumAsync(started);
            }
            HandleAuthFailure(ex, clearStoredRefreshToken: true);
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error("AppStateService", "Offsets bootstrap failed unexpectedly.", ex);
            if (restoreStartedAt is long started)
            {
                await WaitForRestoreMinimumAsync(started);
            }
            TransitionTo(AppState.FatalError, ex.Message);
            return false;
        }
    }

    /// <summary>Heartbeat-time offsets refresh in response to a server version bump.</summary>
    private async Task<RefreshAttemptResult> TryRefreshOffsetsAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            await _offsetsService.RefreshAsync(accessToken, cancellationToken);
            return RefreshAttemptResult.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RefreshAttemptResult.StopLoop;
        }
        catch (AuthApiException ex) when (ex.IsTransient)
        {
            TransitionTo(AppState.Unreachable, ex.Message);
            return RefreshAttemptResult.RetryLater;
        }
        catch (AuthApiException ex)
        {
            HandleLockout(ex);
            return RefreshAttemptResult.StopLoop;
        }
        catch (Exception ex)
        {
            AppLog.Error("AppStateService", "Heartbeat offsets refresh failed.", ex);
            return RefreshAttemptResult.RetryLater;
        }
    }

    private void StartHeartbeatLoop(TimeSpan initialDelay)
    {
        StopHeartbeatLoop();

        var cancellation = new CancellationTokenSource();
        lock (_syncRoot)
        {
            _heartbeatLoopCancellation = cancellation;
        }

        _ = Task.Run(() => RunHeartbeatLoopAsync(initialDelay, cancellation.Token));
    }

    private void StopHeartbeatLoop()
    {
        CancellationTokenSource? cancellation;

        lock (_syncRoot)
        {
            cancellation = _heartbeatLoopCancellation;
            _heartbeatLoopCancellation = null;
        }

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        StopHeartbeatLoop();
    }

    private async Task RunHeartbeatLoopAsync(TimeSpan initialDelay, CancellationToken cancellationToken)
    {
        var delay = initialDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var token = AccessToken;
            if (token is null)
            {
                TransitionTo(AppState.Login);
                return;
            }

            try
            {
                var status = await _authService.GetSessionStatusAsync(token, cancellationToken);

                if (CurrentState == AppState.Unreachable)
                {
                    TransitionTo(AppState.Running);
                }

                if (!string.IsNullOrEmpty(status.OffsetsVersion) &&
                    !string.Equals(status.OffsetsVersion, _offsetsService.Version, StringComparison.Ordinal))
                {
                    var versionChangeResult = await TryRefreshOffsetsAsync(token, cancellationToken);
                    if (versionChangeResult == RefreshAttemptResult.StopLoop)
                    {
                        return;
                    }
                    if (versionChangeResult == RefreshAttemptResult.RetryLater)
                    {
                        delay = _heartbeatRetryInterval;
                        continue;
                    }
                }

                var now = DateTimeOffset.UtcNow;
                if (IsProactiveRefreshDue(now) && CanAttemptProactiveRefresh(now))
                {
                    var refreshResult = await TryRefreshOnceAsync(cancellationToken, "Proactive access-token");
                    if (refreshResult == RefreshAttemptResult.StopLoop)
                    {
                        return;
                    }

                    delay = refreshResult == RefreshAttemptResult.RetryLater
                        ? _heartbeatRetryInterval
                        : _heartbeatInterval;
                    continue;
                }

                delay = _heartbeatInterval;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (AuthApiException ex) when (ex.IsTransient)
            {
                TransitionTo(AppState.Unreachable, ex.Message);
                delay = _heartbeatRetryInterval;
            }
            catch (AuthApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && ex.Reason == "expired")
            {
                var refreshResult = await TryRefreshOnceAsync(cancellationToken, "Heartbeat expired");
                if (refreshResult == RefreshAttemptResult.StopLoop)
                {
                    return;
                }

                delay = refreshResult == RefreshAttemptResult.RetryLater
                    ? _heartbeatRetryInterval
                    : _heartbeatInterval;
            }
            catch (AuthApiException ex)
            {
                HandleLockout(ex);
                return;
            }
            catch (Exception ex)
            {
                TransitionTo(AppState.FatalError, ex.Message);
                return;
            }
        }
    }

    private bool IsProactiveRefreshDue(DateTimeOffset now)
    {
        if (AccessToken is null || _accessTokenExpiresAt is null)
        {
            return false;
        }

        return now >= _accessTokenExpiresAt.Value - _proactiveRefreshLeadTime;
    }

    private bool CanAttemptProactiveRefresh(DateTimeOffset now)
    {
        return now >= _nextProactiveRefreshAttemptAt;
    }

    private async Task<RefreshAttemptResult> TryRefreshOnceAsync(
        CancellationToken cancellationToken,
        string reason)
    {
        if (!TryEnsureHwidHash(out var hwidHash))
        {
            return RefreshAttemptResult.StopLoop;
        }

        var refreshToken = _authService.LoadRefreshToken();
        if (refreshToken is null)
        {
            TransitionTo(AppState.Login);
            return RefreshAttemptResult.StopLoop;
        }

        try
        {
            var tokens = await _authService.RefreshAsync(refreshToken, hwidHash);
            ApplyTokenPair(tokens);
            AppLog.Info("AppStateService", $"{reason} refresh succeeded.");
            return RefreshAttemptResult.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RefreshAttemptResult.StopLoop;
        }
        catch (AuthApiException ex) when (ex.IsTransient)
        {
            _nextProactiveRefreshAttemptAt = DateTimeOffset.UtcNow + _proactiveRefreshRetryInterval;
            TransitionTo(AppState.Unreachable, ex.Message);
            return RefreshAttemptResult.RetryLater;
        }
        catch (AuthApiException ex)
        {
            HandleAuthFailure(ex, clearStoredRefreshToken: true);
            return RefreshAttemptResult.StopLoop;
        }
        catch (Exception ex)
        {
            TransitionTo(AppState.FatalError, ex.Message);
            return RefreshAttemptResult.StopLoop;
        }
    }

    private bool TryEnsureHwidHash(out string hwidHash)
    {
        if (!string.IsNullOrWhiteSpace(_hwidHash))
        {
            hwidHash = _hwidHash;
            return true;
        }

        try
        {
            _hwidHash = _hwidProvider.GetHash();
            hwidHash = _hwidHash;
            AppLog.Info("AppStateService", "HWID hash resolved.");
            return true;
        }
        catch (Exception ex)
        {
            hwidHash = string.Empty;
            AppLog.Error("AppStateService", "HWID collection failed.", ex);
            TransitionTo(AppState.FatalError, $"HWID collection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Maps an auth failure from startup/API calls to the appropriate app state.
    /// Preserves backward-compat: "revoked" → AppState.Revoked; new reasons → AppState.LockedOut.
    /// </summary>
    private void HandleAuthFailure(AuthApiException ex, bool clearStoredRefreshToken)
    {
        if (ex.IsTransient)
        {
            TransitionTo(AppState.Unreachable, ex.Message);
            return;
        }

        if (clearStoredRefreshToken)
        {
            _authService.DeleteRefreshToken();
            ClearRuntimeTokens();
        }

        switch (ex.Reason)
        {
            case "expired":
                TransitionTo(AppState.Login);
                break;
            case "revoked":
                TransitionTo(AppState.Revoked, "Access revoked.");
                break;
            case "unlicensed":
                TransitionTo(AppState.Login, "Purchase required.");
                break;
            case "hwid_mismatch":
                LockoutReason = ex.Reason;
                TransitionTo(AppState.LockedOut, ex.Reason);
                break;
            case "banned":
            case "chargeback":
            case "compromised":
                LockoutReason = ex.Reason;
                TransitionTo(AppState.LockedOut, ex.Reason);
                break;
            default:
                TransitionTo(AppState.Login, ex.Message);
                break;
        }
    }

    /// <summary>
    /// Maps a heartbeat failure to a lockout state. All non-transient failures lock the user out.
    /// </summary>
    private void HandleLockout(AuthApiException ex)
    {
        StopHeartbeatLoop();
        _authService.DeleteRefreshToken();
        ClearRuntimeTokens();

        switch (ex.Reason)
        {
            case "expired":
                TransitionTo(AppState.Login);
                break;
            case "revoked":
            case "banned":
            case "chargeback":
            case "compromised":
            case "hwid_mismatch":
            case "unlicensed":
                LockoutReason = ex.Reason;
                TransitionTo(AppState.LockedOut, ex.Reason);
                break;
            default:
                LockoutReason = "revoked";
                TransitionTo(AppState.LockedOut, "revoked");
                break;
        }
    }

    private static async Task WaitForRestoreMinimumAsync(long startedAt)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var remaining = RestoreSessionMinimumDuration - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private void TransitionTo(AppState state, string? error = null)
    {
        AppLog.Info("AppStateService", $"TransitionTo {state}. error={error ?? "none"}.");
        Action<AppState>? handler;

        lock (_syncRoot)
        {
            CurrentState = state;
            ErrorMessage = error;
            handler = StateChanged;
        }

        handler?.Invoke(state);
    }
}
