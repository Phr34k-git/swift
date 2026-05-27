using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class FishingReelStatsTests
{
    [Fact]
    public void RecordOutcome_CountsCaughtLostAndSuccessRate()
    {
        var stats = FishingReelStats.Empty
            .Record(FishingReelOutcome.Caught)
            .Record(FishingReelOutcome.Lost)
            .Record(FishingReelOutcome.Caught);

        Assert.Equal(2, stats.Caught);
        Assert.Equal(1, stats.Lost);
        Assert.Equal(3, stats.Total);
        Assert.Equal(66.66666666666666, stats.SuccessRatePercent, 10);
    }

    [Fact]
    public void RecordOutcome_IgnoresSkippedOrUnknownOutcomes()
    {
        var stats = FishingReelStats.Empty
            .Record(FishingReelOutcome.Skipped)
            .Record(FishingReelOutcome.None);

        Assert.Equal(0, stats.Caught);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.SuccessRatePercent);
    }

    [Fact]
    public void ResolveReelOutcome_RecordsExactlyOnce()
    {
        var first = Tracking1FishingTracker.ResolveReelOutcome(
            outcomeResolved: false,
            stats: FishingReelStats.Empty,
            completionReached: true);

        var second = Tracking1FishingTracker.ResolveReelOutcome(
            outcomeResolved: first.OutcomeResolved,
            stats: first.Stats,
            completionReached: false);

        Assert.True(first.OutcomeResolved);
        Assert.Equal(new FishingReelStats(1, 0), first.Stats);
        Assert.Equal(new FishingReelStats(1, 0), second.Stats);
    }

    [Fact]
    public void ResolveReelOutcome_RecordsLostWhenCompletionWasNotReached()
    {
        var result = Tracking1FishingTracker.ResolveReelOutcome(
            outcomeResolved: false,
            stats: FishingReelStats.Empty,
            completionReached: false);

        Assert.True(result.OutcomeResolved);
        Assert.Equal(new FishingReelStats(0, 1), result.Stats);
    }
}
