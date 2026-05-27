using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class RodProfileTests
{
    [Fact]
    public void Default_HasDefaultKindAndNoDelay()
    {
        var profile = new DefaultRodProfile();
        Assert.Equal(RodKind.Default, profile.Kind);
        Assert.Equal(0, profile.FishingActionDelayMs);
    }

    [Fact]
    public void Default_DoesNotTransformHold()
    {
        var profile = new DefaultRodProfile();
        Assert.True(profile.TransformHold(true, 90.0));
        Assert.False(profile.TransformHold(false, 90.0));
    }

    [Fact]
    public void Default_AdjustTarget_ReturnsMetricsUnchanged()
    {
        var profile = new DefaultRodProfile();
        var metrics = new ReelMetrics(0.6, 0.5, 0.3);
        Assert.Equal(metrics, profile.AdjustTarget(metrics, new NoteTarget(0.2, 0.1)));
        Assert.Equal(metrics, profile.AdjustTarget(metrics, null));
    }

    [Theory]
    [InlineData(RodKind.Default, typeof(DefaultRodProfile))]
    [InlineData(RodKind.BellonaWaraxe, typeof(BellonaWaraxeRodProfile))]
    [InlineData(RodKind.Pinion, typeof(PinionRodProfile))]
    [InlineData(RodKind.Tranquility, typeof(TranquilityRodProfile))]
    [InlineData(RodKind.Dreambreaker, typeof(DreambreakerRodProfile))]
    [InlineData(RodKind.Requiem, typeof(RequiemRodProfile))]
    public void For_ReturnsMatchingProfile(RodKind kind, System.Type expected)
    {
        Assert.IsType(expected, RodProfile.For(kind));
    }

    [Fact]
    public void Requiem_AppliesOneSixtyDelay()
    {
        Assert.Equal(160, RodProfile.For(RodKind.Requiem).FishingActionDelayMs);
    }

    [Theory]
    [InlineData(true, 39.9, true)]
    [InlineData(true, 40.0, false)]
    [InlineData(false, 40.0, true)]
    [InlineData(true, 80.0, false)]
    [InlineData(true, null, true)]
    public void Dreambreaker_InvertsHoldAtOrAbove40Percent(bool desired, double? progress, bool expected)
    {
        var profile = new DreambreakerRodProfile();
        Assert.Equal(expected, profile.TransformHold(desired, progress));
    }
}
