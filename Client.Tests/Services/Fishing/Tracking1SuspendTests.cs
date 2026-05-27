using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class Tracking1SuspendTests
{
    [Fact]
    public void Suspend_LeavesTrackerRunningButStopsFishingWork()
    {
        using var tracker = new Tracking1FishingTracker();

        tracker.Start();
        tracker.Suspend("Auto Aquarium running.");

        Assert.True(tracker.Status.IsRunning);
        Assert.Equal("SUSPENDED", tracker.Status.Phase);
        Assert.Equal("Auto Aquarium running.", tracker.Status.Message);

        tracker.Resume();
        tracker.Stop();
    }
}
