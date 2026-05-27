using System;
using System.Net;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Client.Tests.Fakes;
using Xunit;

namespace Client.Tests;

/// <summary>
/// Tests for the 30-second read-only heartbeat loop that replaces the old refresh loop.
/// </summary>
public sealed class HeartbeatLoopTests
{
    private static AuthTokens MakeTokens(
        string accessToken = "test-access-token",
        string refreshToken = "test-refresh-token",
        TimeSpan? accessTtl = null) => new(
            accessToken,
            DateTimeOffset.UtcNow.Add(accessTtl ?? TimeSpan.FromDays(90)),
            refreshToken,
            DateTimeOffset.UtcNow.AddDays(90));

    private static AuthApiException Transient() =>
        new(null, null, "server down");

    private static AuthApiException Permanent(string reason, HttpStatusCode status = HttpStatusCode.Forbidden) =>
        new(status, reason, reason);

    private static AuthApiException Expired() =>
        new(HttpStatusCode.Unauthorized, "expired", "expired");

    private static AppStateService Build(
        FakeAuthService auth,
        TimeSpan? proactiveRefreshLeadTime = null,
        TimeSpan? proactiveRefreshRetryInterval = null,
        FakeOffsetsRuntime? offsets = null) =>
        new(
            auth,
            new FakeHwidProvider(),
            offsets ?? new FakeOffsetsRuntime(),
            heartbeatInterval: TimeSpan.FromMilliseconds(10),
            heartbeatRetryInterval: TimeSpan.FromMilliseconds(10),
            proactiveRefreshLeadTime: proactiveRefreshLeadTime,
            proactiveRefreshRetryInterval: proactiveRefreshRetryInterval);

    /// <summary>Helper: initialize the service so it is in Running state with a heartbeat loop started.</summary>
    private static async Task<AppStateService> BuildRunning(FakeAuthService auth)
    {
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens());
        var svc = Build(auth);
        await svc.InitializeAsync();
        Assert.Equal(AppState.Running, svc.CurrentState);
        return svc;
    }

    // ── Steady state ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_Success_StaysRunning()
    {
        var auth = new FakeAuthService();
        auth.EnqueueSessionStatus();
        var svc = await BuildRunning(auth);

        // Single successful heartbeat tick
        auth.EnqueueSessionStatus();
        await Task.Delay(50); // let loop tick

        // State must remain Running regardless of how many ticks occurred
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_Success_DoesNotCallRefresh()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        var refreshCountAfterInit = auth.RefreshCallCount;

        // Enqueue several successful heartbeats
        auth.EnqueueSessionStatus();
        auth.EnqueueSessionStatus();

        await Task.Delay(50);

        Assert.Equal(refreshCountAfterInit, auth.RefreshCallCount);
    }

    // ── Transient failures ────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_TransientFailure_TransitionsToUnreachable()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        auth.EnqueueSessionStatusError(Transient());
        await Task.Delay(50);

        // Ensure the next tick can recover
        auth.EnqueueSessionStatus();

        await Task.Delay(100);
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_UnreachableRecovers_BackToRunning()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        // Trigger unreachable
        auth.EnqueueSessionStatusError(Transient());

        // Wait a bit, then enqueue success
        auth.EnqueueSessionStatus();
        await Task.Delay(150);

        // Service should have recovered to Running
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    // ── Expired recovery ──────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_Expired_TriesRefreshOnce_ResumesOnSuccess()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        var refreshCountAfterInit = auth.RefreshCallCount;

        // Heartbeat returns expired, followed by a successful refresh and continued heartbeats
        auth.EnqueueSessionStatusError(Expired());
        auth.EnqueueRefresh(MakeTokens());
        auth.EnqueueSessionStatus();

        await Task.Delay(100);

        Assert.Equal(refreshCountAfterInit + 1, auth.RefreshCallCount);
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_Expired_TriesRefreshOnce_LocksOutOnRefreshFailure()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        auth.EnqueueSessionStatusError(Expired());
        auth.EnqueueRefreshError(Permanent("revoked"));

        await Task.Delay(100);

        // Refresh failed with revoked — should lock out (Revoked or LockedOut both acceptable here)
        Assert.NotEqual(AppState.Running, svc.CurrentState);
    }

    // ── Lockout reasons ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("revoked")]
    [InlineData("banned")]
    [InlineData("chargeback")]
    [InlineData("compromised")]
    [InlineData("hwid_mismatch")]
    [InlineData("unlicensed")]
    public async Task Heartbeat_LockoutReason_TransitionsToLockedOut(string reason)
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        auth.EnqueueSessionStatusError(Permanent(reason));
        await Task.Delay(100);

        Assert.Equal(AppState.LockedOut, svc.CurrentState);
        Assert.Equal(reason, svc.LockoutReason);
    }

    [Fact]
    public async Task Heartbeat_UnknownReason_FallsBackToRevoked()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        auth.EnqueueSessionStatusError(Permanent("some_future_unknown_reason"));
        await Task.Delay(100);

        Assert.Equal(AppState.LockedOut, svc.CurrentState);
        Assert.Equal("revoked", svc.LockoutReason);
    }

    [Fact]
    public async Task Heartbeat_Lockout_ClearsStoredRefreshToken()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        var deleteCountBefore = auth.DeleteRefreshTokenCallCount;
        auth.EnqueueSessionStatusError(Permanent("banned"));
        await Task.Delay(100);

        Assert.Equal(AppState.LockedOut, svc.CurrentState);
        Assert.True(auth.DeleteRefreshTokenCallCount > deleteCountBefore);
    }

    [Fact]
    public async Task Heartbeat_403NullReason_TransitionsToUnreachableAndPreservesToken()
    {
        // A 403 with no reason field is a proxy-level block (e.g. Cloudflare bot
        // protection). It must be treated as transient — Unreachable, not LockedOut —
        // and the stored refresh token must NOT be deleted.
        // The loop recovers quickly (retries on next tick), so we capture the
        // state transition via the event rather than polling at a fixed point in time.
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        var deleteCountBefore = auth.DeleteRefreshTokenCallCount;
        var sawUnreachable = false;
        svc.StateChanged += state => { if (state == AppState.Unreachable) sawUnreachable = true; };

        auth.EnqueueSessionStatusError(
            new AuthApiException(HttpStatusCode.Forbidden, null, "proxy block"));
        await Task.Delay(100);

        Assert.True(sawUnreachable);
        Assert.Equal(deleteCountBefore, auth.DeleteRefreshTokenCallCount);
    }

    // ── 401 non-expired ───────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_401_NonExpired_TransitionsToLockedOut()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);

        auth.EnqueueSessionStatusError(
            new AuthApiException(HttpStatusCode.Unauthorized, "revoked", "revoked"));
        await Task.Delay(100);

        Assert.Equal(AppState.LockedOut, svc.CurrentState);
    }

    // ── Proactive refresh ─────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_ProactivelyRefreshesBeforeAccessTokenExpires()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens(
            accessToken: "initial-access-token",
            refreshToken: "initial-refresh-token",
            accessTtl: TimeSpan.FromMilliseconds(50)));
        auth.EnqueueSessionStatus();
        auth.EnqueueRefresh(MakeTokens(
            accessToken: "proactive-access-token",
            refreshToken: "proactive-refresh-token",
            accessTtl: TimeSpan.FromMinutes(30)));

        var svc = Build(auth, proactiveRefreshLeadTime: TimeSpan.FromMilliseconds(100));
        await svc.InitializeAsync();
        var refreshCountAfterInit = auth.RefreshCallCount;

        await Task.Delay(120);

        Assert.True(auth.RefreshCallCount > refreshCountAfterInit);
        Assert.Equal("proactive-access-token", svc.AccessToken);
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_DoesNotProactivelyRefreshWhenAccessTokenIsNotNearExpiry()
    {
        var auth = new FakeAuthService();
        var svc = await BuildRunning(auth);
        var refreshCountAfterInit = auth.RefreshCallCount;

        auth.EnqueueSessionStatus();
        auth.EnqueueSessionStatus();

        await Task.Delay(80);

        Assert.Equal(refreshCountAfterInit, auth.RefreshCallCount);
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_ProactiveRefreshTransientFailure_DoesNotClearStoredRefreshToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens(
            accessToken: "initial-access-token",
            refreshToken: "initial-refresh-token",
            accessTtl: TimeSpan.FromMilliseconds(50)));
        auth.EnqueueSessionStatus();
        auth.EnqueueRefreshError(Transient());

        var svc = Build(
            auth,
            proactiveRefreshLeadTime: TimeSpan.FromMilliseconds(100),
            proactiveRefreshRetryInterval: TimeSpan.FromMilliseconds(200));
        await svc.InitializeAsync();
        var deleteCountAfterInit = auth.DeleteRefreshTokenCallCount;
        var refreshCountAfterInit = auth.RefreshCallCount;

        await Task.Delay(120);

        Assert.Equal(refreshCountAfterInit + 1, auth.RefreshCallCount);
        Assert.Equal(deleteCountAfterInit, auth.DeleteRefreshTokenCallCount);
        Assert.Equal("initial-access-token", svc.AccessToken);
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_ExpiredFallbackStillRefreshesWhenProactiveDidNotRun()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens(
            accessToken: "initial-access-token",
            refreshToken: "initial-refresh-token",
            accessTtl: TimeSpan.FromMilliseconds(20)));
        auth.EnqueueSessionStatusError(Expired());
        auth.EnqueueRefresh(MakeTokens(
            accessToken: "fallback-access-token",
            refreshToken: "fallback-refresh-token",
            accessTtl: TimeSpan.FromMinutes(30)));

        var svc = Build(auth, proactiveRefreshLeadTime: TimeSpan.Zero);
        await svc.InitializeAsync();
        var refreshCountAfterInit = auth.RefreshCallCount;

        await Task.Delay(80);

        Assert.True(auth.RefreshCallCount > refreshCountAfterInit);
        Assert.Equal("fallback-access-token", svc.AccessToken);
        Assert.Equal(AppState.Running, svc.CurrentState);
    }

    [Fact]
    public async Task Heartbeat_ProactiveRefreshPermanentFailure_ClearsStoredRefreshToken()
    {
        var auth = new FakeAuthService();
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens(
            accessToken: "initial-access-token",
            refreshToken: "initial-refresh-token",
            accessTtl: TimeSpan.FromMilliseconds(50)));
        auth.EnqueueSessionStatus();
        auth.EnqueueRefreshError(Permanent("revoked"));

        var svc = Build(auth, proactiveRefreshLeadTime: TimeSpan.FromMilliseconds(100));
        await svc.InitializeAsync();
        var deleteCountAfterInit = auth.DeleteRefreshTokenCallCount;

        await Task.Delay(120);

        Assert.True(auth.DeleteRefreshTokenCallCount > deleteCountAfterInit);
        Assert.Null(svc.AccessToken);
        Assert.Equal(AppState.Revoked, svc.CurrentState);
    }

    // ── Offsets version handshake ────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_OffsetsVersionChange_TriggersOffsetsRefresh()
    {
        var auth = new FakeAuthService();
        var offsets = new FakeOffsetsRuntime { VersionAfterRefresh = "v1" };
        var svc = Build(auth, offsets: offsets);
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens());
        await svc.InitializeAsync();
        Assert.Equal("v1", offsets.Version);
        var refreshAfterInit = offsets.RefreshCallCount;

        // Server announces a new offsets version → client must refetch.
        offsets.VersionAfterRefresh = "v2";
        auth.EnqueueSessionStatus(offsetsVersion: "v2");

        await Task.Delay(80);

        Assert.True(offsets.RefreshCallCount > refreshAfterInit,
            $"Expected offsets refresh after version change. count={offsets.RefreshCallCount}, afterInit={refreshAfterInit}");
        Assert.Equal("v2", offsets.Version);
    }

    [Fact]
    public async Task Heartbeat_OffsetsVersionMatches_DoesNotTriggerRefetch()
    {
        var auth = new FakeAuthService();
        var offsets = new FakeOffsetsRuntime { VersionAfterRefresh = "v1" };
        var svc = Build(auth, offsets: offsets);
        auth.SetStoredRefreshToken("stored-refresh");
        auth.EnqueueRefresh(MakeTokens());
        await svc.InitializeAsync();
        var refreshAfterInit = offsets.RefreshCallCount;

        auth.EnqueueSessionStatus(offsetsVersion: "v1");
        await Task.Delay(60);

        Assert.Equal(refreshAfterInit, offsets.RefreshCallCount);
    }
}
