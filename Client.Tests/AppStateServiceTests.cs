using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Client.Tests.Fakes;
using Xunit;

namespace Client.Tests;

public sealed class AppStateServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static AuthTokens MakeTokens() => new(
        "test-access-token",
        DateTimeOffset.UtcNow.AddMinutes(2),
        "test-refresh-token",
        DateTimeOffset.UtcNow.AddDays(90));

    private static AuthApiException Transient() =>
        new(null, null, "server down");

    private static AuthApiException Permanent(string reason) =>
        new(HttpStatusCode.Unauthorized, reason, reason);

    private static AppStateService Build(FakeAuthService auth, FakeOffsetsRuntime? offsets = null) =>
        new(auth, new FakeHwidProvider(), offsets ?? new FakeOffsetsRuntime());

    // ── Startup state ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NoStoredToken_StartsAtLogin()
    {
        var auth = new FakeAuthService();
        var svc = Build(auth);

        Assert.Equal(AppState.Login, svc.CurrentState);
    }

    [Fact]
    public void Constructor_StoredToken_StartsAtInitializing()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        var svc = Build(auth);

        Assert.Equal(AppState.Initializing, svc.CurrentState);
    }

    // ── InitializeAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_NoStoredToken_TransitionsToLogin()
    {
        var auth = new FakeAuthService();
        var svc = Build(auth);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Login, svc.CurrentState);
    }

    [Fact]
    public async Task InitializeAsync_NoStoredToken_DoesNotShowInitializingState()
    {
        var auth = new FakeAuthService();
        var svc = Build(auth);
        var sawInitializing = false;
        svc.StateChanged += state =>
        {
            if (state == AppState.Initializing)
            {
                sawInitializing = true;
            }
        };

        await svc.InitializeAsync();

        Assert.False(sawInitializing);
    }

    [Fact]
    public async Task InitializeAsync_StoredToken_Success_TransitionsToRunning()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var svc = Build(auth);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Running, svc.CurrentState);
        Assert.Equal("test-access-token", svc.AccessToken);
    }

    [Fact]
    public async Task InitializeAsync_StoredToken_Success_WaitsAtLeastRestoreMinimum()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var svc = Build(auth);

        var elapsed = Stopwatch.StartNew();
        await svc.InitializeAsync();
        elapsed.Stop();

        Assert.True(
            elapsed.Elapsed >= TimeSpan.FromMilliseconds(575),
            $"Expected restore state to remain visible for about 600ms, actual {elapsed.Elapsed.TotalMilliseconds:F0}ms.");
    }

    // Bug 1 — transient must go to Unreachable, not Login
    [Fact]
    public async Task InitializeAsync_TransientRefreshFailure_TransitionsToUnreachable()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefreshError(Transient());
        var svc = Build(auth);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Unreachable, svc.CurrentState);
    }

    // Bug 3 — transient must NOT clear the stored refresh token
    [Fact]
    public async Task InitializeAsync_TransientRefreshFailure_DoesNotDeleteStoredToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefreshError(Transient());
        var svc = Build(auth);

        await svc.InitializeAsync();

        Assert.Equal(0, auth.DeleteRefreshTokenCallCount);
    }

    [Fact]
    public async Task InitializeAsync_RevokedReason_TransitionsToRevoked()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefreshError(Permanent("revoked"));
        var svc = Build(auth);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Revoked, svc.CurrentState);
        Assert.Equal(1, auth.DeleteRefreshTokenCallCount);
    }

    [Fact]
    public async Task InitializeAsync_StoredToken_PermanentFailure_WaitsAtLeastRestoreMinimum()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefreshError(Permanent("expired"));
        var svc = Build(auth);

        var elapsed = Stopwatch.StartNew();
        await svc.InitializeAsync();
        elapsed.Stop();

        Assert.True(
            elapsed.Elapsed >= TimeSpan.FromMilliseconds(575),
            $"Expected restore state to remain visible for about 600ms, actual {elapsed.Elapsed.TotalMilliseconds:F0}ms.");
    }

    [Fact]
    public async Task InitializeAsync_HwidMismatch_TransitionsToLockedOutAndClearsToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefreshError(Permanent("hwid_mismatch"));
        var svc = Build(auth);

        await svc.InitializeAsync();

        Assert.Equal(AppState.LockedOut, svc.CurrentState);
        Assert.Equal("hwid_mismatch", svc.LockoutReason);
        Assert.Equal(1, auth.DeleteRefreshTokenCallCount);
    }

    // ── CompleteLoginAsync ────────────────────────────────────────────────────

    // Bug 1 — transient OAuth exchange must go to Unreachable, not Login
    [Fact]
    public async Task CompleteLoginAsync_TransientFailure_TransitionsToUnreachable()
    {
        var auth = new FakeAuthService();
        auth.EnqueueExchangeError(Transient());
        var svc = Build(auth);

        await svc.CompleteLoginAsync("code");

        Assert.Equal(AppState.Unreachable, svc.CurrentState);
    }

    [Fact]
    public async Task CompleteLoginAsync_HwidMismatch_TransitionsToLockedOut()
    {
        var auth = new FakeAuthService();
        auth.EnqueueExchangeError(Permanent("hwid_mismatch"));
        var svc = Build(auth);

        await svc.CompleteLoginAsync("code");

        Assert.Equal(AppState.LockedOut, svc.CurrentState);
        Assert.Equal("hwid_mismatch", svc.LockoutReason);
    }

    // Bug 3 — transient exchange failure must not clear stored token
    [Fact]
    public async Task CompleteLoginAsync_TransientFailure_DoesNotDeleteStoredToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("existing");
        auth.EnqueueExchangeError(Transient());
        var svc = Build(auth);

        await svc.CompleteLoginAsync("code");

        Assert.Equal(0, auth.DeleteRefreshTokenCallCount);
    }

    // ── CompleteCredentialsLoginAsync ─────────────────────────────────────────

    // Bug 1 (credential path) — transient failure must not change state and must re-throw
    [Fact]
    public async Task CompleteCredentialsLoginAsync_TransientFailure_DoesNotChangeState()
    {
        var auth = new FakeAuthService(); // no stored token → InitializeAsync → Login
        var svc = Build(auth);
        await svc.InitializeAsync();
        Assert.Equal(AppState.Login, svc.CurrentState);

        auth.EnqueueLoginError(Transient());

        await Assert.ThrowsAsync<AuthApiException>(
            () => svc.CompleteCredentialsLoginAsync("user", "pass"));

        // State must remain Login, not flip to Unreachable (which would hide the login form)
        Assert.Equal(AppState.Login, svc.CurrentState);
    }

    // Bug 3 (credential path) — transient failure must not delete the stored refresh token
    [Fact]
    public async Task CompleteCredentialsLoginAsync_TransientFailure_DoesNotDeleteStoredToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("existing");
        auth.EnqueueRefresh(MakeTokens()); // for InitializeAsync
        var svc = Build(auth);
        await svc.InitializeAsync(); // Running

        auth.EnqueueLoginError(Transient());

        await Assert.ThrowsAsync<AuthApiException>(
            () => svc.CompleteCredentialsLoginAsync("user", "pass"));

        Assert.Equal(0, auth.DeleteRefreshTokenCallCount);
    }

    [Fact]
    public async Task CompleteCredentialsLoginAsync_TransientFailure_Rethrows()
    {
        var auth = new FakeAuthService();
        var svc = Build(auth);
        await svc.InitializeAsync();

        var transient = Transient();
        auth.EnqueueLoginError(transient);

        var thrown = await Assert.ThrowsAsync<AuthApiException>(
            () => svc.CompleteCredentialsLoginAsync("user", "pass"));

        Assert.Same(transient, thrown);
    }

    [Fact]
    public async Task CompleteCredentialsLoginAsync_RevokedReason_TransitionsToRevoked()
    {
        var auth = new FakeAuthService();
        var svc = Build(auth);
        await svc.InitializeAsync();

        auth.EnqueueLoginError(Permanent("revoked"));

        await Assert.ThrowsAsync<AuthApiException>(
            () => svc.CompleteCredentialsLoginAsync("user", "pass"));

        Assert.Equal(AppState.Revoked, svc.CurrentState);
    }

    // ── HandleApiAuthFailure ──────────────────────────────────────────────────

    // Bug 2 — transient must transition to Unreachable, not stop the recovery loop
    [Fact]
    public async Task HandleApiAuthFailure_Transient_TransitionsToUnreachable()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var svc = Build(auth);
        await svc.InitializeAsync(); // Running

        svc.HandleApiAuthFailure(Transient());

        Assert.Equal(AppState.Unreachable, svc.CurrentState);
    }

    // Bug 2 — transient must NOT clear the stored refresh token
    [Fact]
    public async Task HandleApiAuthFailure_Transient_DoesNotClearStoredToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var svc = Build(auth);
        await svc.InitializeAsync();

        svc.HandleApiAuthFailure(Transient());

        Assert.Equal(0, auth.DeleteRefreshTokenCallCount);
    }

    [Fact]
    public async Task HandleApiAuthFailure_Unauthorized_TransitionsToLoginAndClearsToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var svc = Build(auth);
        await svc.InitializeAsync();

        svc.HandleApiAuthFailure(Permanent("expired"));

        Assert.Equal(AppState.Login, svc.CurrentState);
        Assert.Equal(1, auth.DeleteRefreshTokenCallCount);
    }

    // ── Offsets bootstrap & lifecycle ────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_StoredToken_FetchesOffsetsBeforeRunning()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var offsets = new FakeOffsetsRuntime();
        var svc = Build(auth, offsets);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Running, svc.CurrentState);
        Assert.Equal(1, offsets.RefreshCallCount);
        Assert.True(offsets.IsPopulated);
    }

    [Fact]
    public async Task InitializeAsync_OffsetsTransientFailure_TransitionsToUnreachable()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var offsets = new FakeOffsetsRuntime
        {
            ThrowOnRefresh = Transient(),
        };
        var svc = Build(auth, offsets);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Unreachable, svc.CurrentState);
    }

    [Fact]
    public async Task InitializeAsync_OffsetsPermanentFailure_TransitionsToLogin()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var offsets = new FakeOffsetsRuntime
        {
            ThrowOnRefresh = Permanent("expired"),
        };
        var svc = Build(auth, offsets);

        await svc.InitializeAsync();

        Assert.Equal(AppState.Login, svc.CurrentState);
        Assert.Equal(1, auth.DeleteRefreshTokenCallCount);
    }

    [Fact]
    public async Task LogoutAsync_ClearsOffsets()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var offsets = new FakeOffsetsRuntime();
        var svc = Build(auth, offsets);
        await svc.InitializeAsync();
        Assert.True(offsets.IsPopulated);

        await svc.LogoutAsync();

        Assert.True(offsets.ClearCallCount >= 1);
        Assert.False(offsets.IsPopulated);
    }

    [Fact]
    public async Task HandleApiAuthFailure_PermanentFailure_ClearsOffsets()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("token");
        auth.EnqueueRefresh(MakeTokens());
        var offsets = new FakeOffsetsRuntime();
        var svc = Build(auth, offsets);
        await svc.InitializeAsync();
        Assert.True(offsets.IsPopulated);

        svc.HandleApiAuthFailure(Permanent("revoked"));

        Assert.False(offsets.IsPopulated);
    }
}
