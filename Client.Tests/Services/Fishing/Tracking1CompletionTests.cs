using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class Tracking1CompletionTests
{
    [Fact]
    public void UpdateCompletionState_LatchesWhenProgressReachesThreshold()
    {
        var state = Tracking1FishingTracker.UpdateCompletionState(
            completionReached: false,
            maxProgressThisCycle: 42.0,
            progress: 99.5,
            completionThreshold: 99.5);

        Assert.True(state.CompletionReached);
        Assert.Equal(99.5, state.MaxProgressThisCycle);
    }

    [Fact]
    public void UpdateCompletionState_PreservesLatchedCompletionWhenProgressBecomesUnreadable()
    {
        var state = Tracking1FishingTracker.UpdateCompletionState(
            completionReached: true,
            maxProgressThisCycle: 99.5,
            progress: null,
            completionThreshold: 99.5);

        Assert.True(state.CompletionReached);
        Assert.Equal(99.5, state.MaxProgressThisCycle);
    }

    [Fact]
    public void ShouldResetCompletionStateAfterCompletedReelDisappears()
    {
        Assert.True(Tracking1FishingTracker.ShouldResetCompletionStateAfterReelLoss(
            completionReached: true,
            progress: null,
            maxProgressThisCycle: 99.5,
            hasMetrics: false));

        Assert.False(Tracking1FishingTracker.ShouldResetCompletionStateAfterReelLoss(
            completionReached: true,
            progress: 99.5,
            maxProgressThisCycle: 99.5,
            hasMetrics: false));

        Assert.False(Tracking1FishingTracker.ShouldResetCompletionStateAfterReelLoss(
            completionReached: false,
            progress: null,
            maxProgressThisCycle: 0,
            hasMetrics: false));

        Assert.False(Tracking1FishingTracker.ShouldResetCompletionStateAfterReelLoss(
            completionReached: true,
            progress: null,
            maxProgressThisCycle: 99.5,
            hasMetrics: true));
    }

    [Fact]
    public void ShouldResetCompletionStateWhenNewMinigameStartsBeforeReelDisappears()
    {
        // Catch latched at 99.5%; reel ScreenGui never went invisible between
        // catches, but progress restarted near 0 for the next fish.
        Assert.True(Tracking1FishingTracker.ShouldResetCompletionStateAfterReelLoss(
            completionReached: true,
            progress: 5.0,
            maxProgressThisCycle: 99.5,
            hasMetrics: true));
    }

    [Fact]
    public void ShouldNotResetCompletionStateOnSmallProgressDip()
    {
        // Small fluctuations near the peak (e.g., progress bar settling) must
        // not clear the latch — only a clear restart should.
        Assert.False(Tracking1FishingTracker.ShouldResetCompletionStateAfterReelLoss(
            completionReached: true,
            progress: 90.0,
            maxProgressThisCycle: 99.5,
            hasMetrics: true));
    }
}
