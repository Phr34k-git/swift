using Client.Services.Fishing;
using Client.ViewModels;
using Xunit;

namespace Client.Tests.ViewModels;

public sealed class FishingViewModelStatsTests
{
    [Fact]
    public void StatsItems_ShowCaughtLostSuccessRateAndUntrackedSkippedFish()
    {
        var viewModel = new FishingViewModel();

        viewModel.ApplyStatusForTests(new FishingTrackerStatus(
            true,
            "FISHING",
            "Test status.",
            null,
            false,
            new FishingReelStats(2, 1)));

        Assert.Contains(viewModel.StatsItems, item => item.Entry == "Caught" && item.Value == "2");
        Assert.Contains(viewModel.StatsItems, item => item.Entry == "Lost" && item.Value == "1");
        Assert.Contains(viewModel.StatsItems, item => item.Entry == "Success Rate" && item.Value == "66.7%");
        Assert.Contains(viewModel.StatsItems, item => item.Entry == "Fish skipped" && item.Value == "---");
        Assert.DoesNotContain(viewModel.StatsItems, item => item.Entry == "Total counted");
    }
}
