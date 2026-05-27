using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class PinionRodProfileTests
{
    // Bar centered at 0.5, width 0.3 -> half-width 0.15. In-bar X span with the
    // 0.1 note-count padding is [0.25, 0.75].
    private static ReelMetrics Bar() => new(0.5, 0.5, 0.3);

    [Fact]
    public void NullNote_ReturnsMetricsUnchanged()
    {
        var profile = new PinionRodProfile();
        var metrics = Bar();
        Assert.Equal(metrics, profile.AdjustTarget(metrics, null));
    }

    [Fact]
    public void NoteBelowDeadzone_ReturnsMetricsUnchanged()
    {
        var profile = new PinionRodProfile();
        var metrics = Bar();
        // Sy = -25 is below NOTE_DEADZONE (-19.5).
        Assert.Equal(metrics, profile.AdjustTarget(metrics, new NoteTarget(0.9, -25.0)));
    }

    [Fact]
    public void NoteNearFish_TargetsMidpoint()
    {
        var profile = new PinionRodProfile();
        // fish 0.5, note 0.6, distance 0.1 <= fullWidth 0.3 -> midpoint 0.55.
        var result = profile.AdjustTarget(Bar(), new NoteTarget(0.6, 0.0));
        Assert.Equal(0.55, result.FishCenter, 9);
    }

    [Fact]
    public void NoteFarFromFish_TargetsNote()
    {
        var profile = new PinionRodProfile();
        // fish 0.5, note 0.95, distance 0.45 > fullWidth 0.3 -> target the note.
        var result = profile.AdjustTarget(Bar(), new NoteTarget(0.95, 0.0));
        Assert.Equal(0.95, result.FishCenter, 9);
    }

    [Fact]
    public void SevenInBarNotes_ActivateResonance()
    {
        var profile = new PinionRodProfile();
        for (var i = 0; i < 7; i++)
        {
            // Sy in [-0.8, 0.53] and Sx in the bar -> counts one note.
            profile.AdjustTarget(Bar(), new NoteTarget(0.5, 0.0));
            // Sy < -8 clears the per-pass latch so the next note can count.
            profile.AdjustTarget(Bar(), new NoteTarget(0.5, -9.0));
        }

        // Resonance: the target is the note Sx regardless of distance to the fish.
        var result = profile.AdjustTarget(Bar(), new NoteTarget(0.95, 0.0));
        Assert.Equal(0.95, result.FishCenter, 9);
    }

    [Fact]
    public void MissedNote_ResetsCountAndResonance()
    {
        var profile = new PinionRodProfile();
        for (var i = 0; i < 7; i++)
        {
            profile.AdjustTarget(Bar(), new NoteTarget(0.5, 0.0));
            profile.AdjustTarget(Bar(), new NoteTarget(0.5, -9.0));
        }

        // A note outside the bar (0.95 is beyond 0.75) at a counting Sy resets.
        profile.AdjustTarget(Bar(), new NoteTarget(0.95, 0.0));

        // Resonance is now off: a far note targets the note (no resonance pass-through
        // distinction here, but the count is back to 0). Verify via a fresh in-bar pass:
        // after reset, 1 in-bar note should not be resonance, so a near note -> midpoint.
        profile.AdjustTarget(Bar(), new NoteTarget(0.5, -9.0));
        var result = profile.AdjustTarget(Bar(), new NoteTarget(0.6, 0.0));
        Assert.Equal(0.55, result.FishCenter, 9);
    }

    [Fact]
    public void Reset_ClearsResonance()
    {
        var profile = new PinionRodProfile();
        for (var i = 0; i < 7; i++)
        {
            profile.AdjustTarget(Bar(), new NoteTarget(0.5, 0.0));
            profile.AdjustTarget(Bar(), new NoteTarget(0.5, -9.0));
        }

        profile.Reset();

        // After reset, a far note is targeted directly (not via resonance),
        // and a near note resolves to the midpoint — proving resonance is off.
        var result = profile.AdjustTarget(Bar(), new NoteTarget(0.6, 0.0));
        Assert.Equal(0.55, result.FishCenter, 9);
    }
}
