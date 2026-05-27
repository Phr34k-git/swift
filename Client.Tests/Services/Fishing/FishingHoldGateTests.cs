using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class FishingHoldGateTests
{
    [Fact]
    public void FirstHold_ReturnsPress_AndMarksHeld()
    {
        var gate = new FishingHoldGate();
        Assert.Equal(FishingHoldAction.Press, gate.Decide(true, 1000, 160));
        Assert.True(gate.Held);
    }

    [Fact]
    public void SameState_ReturnsNone()
    {
        var gate = new FishingHoldGate();
        gate.Decide(true, 1000, 160);
        Assert.Equal(FishingHoldAction.None, gate.Decide(true, 1200, 160));
    }

    [Fact]
    public void FlipWithinDelayWindow_IsGated()
    {
        var gate = new FishingHoldGate();
        gate.Decide(true, 1000, 160);
        // 1100 - 1000 = 100 < 160 -> gated.
        Assert.Equal(FishingHoldAction.None, gate.Decide(false, 1100, 160));
        Assert.True(gate.Held);
    }

    [Fact]
    public void FlipAfterDelayWindow_IsAllowed()
    {
        var gate = new FishingHoldGate();
        gate.Decide(true, 1000, 160);
        gate.Decide(false, 1100, 160); // gated, no state change
        // 1200 - 1000 = 200 >= 160 -> allowed.
        Assert.Equal(FishingHoldAction.Release, gate.Decide(false, 1200, 160));
        Assert.False(gate.Held);
    }

    [Fact]
    public void ZeroDelay_NeverGates()
    {
        var gate = new FishingHoldGate();
        Assert.Equal(FishingHoldAction.Press, gate.Decide(true, 1000, 0));
        Assert.Equal(FishingHoldAction.Release, gate.Decide(false, 1000, 0));
        Assert.Equal(FishingHoldAction.Press, gate.Decide(true, 1000, 0));
    }

    [Fact]
    public void ForceRelease_WhenHeld_ReturnsRelease()
    {
        var gate = new FishingHoldGate();
        gate.Decide(true, 1000, 160);
        Assert.Equal(FishingHoldAction.Release, gate.ForceRelease(1010));
        Assert.False(gate.Held);
    }

    [Fact]
    public void ForceRelease_WhenNotHeld_ReturnsNone()
    {
        var gate = new FishingHoldGate();
        Assert.Equal(FishingHoldAction.None, gate.ForceRelease(1010));
    }

    [Fact]
    public void Reset_AllowsImmediateActionAgain()
    {
        var gate = new FishingHoldGate();
        gate.Decide(true, 1000, 160);
        gate.Reset();
        Assert.False(gate.Held);
        // After reset the next action is ungated even within 160ms of the old one.
        Assert.Equal(FishingHoldAction.Press, gate.Decide(true, 1050, 160));
    }
}
