using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class Tracking1AutoTotemBoundaryTests
{
    // Bug 1: a queued totem must be allowed to run once a fish is caught and
    // the post-catch settle delay has elapsed. Before the original fix the
    // boundary keyed off the reel ScreenGui, which stays enabled on the
    // post-catch screen, so a pending totem deferred forever.
    [Fact]
    public void Boundary_OpensAfterCompletionLatchedAndSettled()
    {
        Assert.True(Tracking1FishingTracker.ComputeAutoTotemBoundary(
            completionLatched: true,
            settleElapsed: true,
            perfectCastingHolding: false,
            castBarSeen: false));
    }

    // Bug 2: the totem workflow must not start while the macro is casting,
    // waiting for a bite, or in a live minigame (no completed catch yet).
    [Fact]
    public void Boundary_ClosedWhileFishingOrCasting()
    {
        Assert.False(Tracking1FishingTracker.ComputeAutoTotemBoundary(
            completionLatched: false,
            settleElapsed: false,
            perfectCastingHolding: false,
            castBarSeen: false));
    }

    // Bug 3 (this fix): completion latches when progress crosses the catch
    // threshold, but the reel minigame can still be visibly on screen at that
    // instant. Until the post-catch settle delay elapses the boundary must
    // stay closed so the workflow does not interrupt an active fight.
    [Fact]
    public void Boundary_ClosedDuringPostCatchSettleDelay()
    {
        Assert.False(Tracking1FishingTracker.ComputeAutoTotemBoundary(
            completionLatched: true,
            settleElapsed: false,
            perfectCastingHolding: false,
            castBarSeen: false));
    }

    [Fact]
    public void Boundary_ClosedWhilePerfectCastInProgress()
    {
        Assert.False(Tracking1FishingTracker.ComputeAutoTotemBoundary(
            completionLatched: true,
            settleElapsed: true,
            perfectCastingHolding: true,
            castBarSeen: false));

        Assert.False(Tracking1FishingTracker.ComputeAutoTotemBoundary(
            completionLatched: true,
            settleElapsed: true,
            perfectCastingHolding: false,
            castBarSeen: true));
    }
}
