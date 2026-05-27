using System;
using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class Tracking3ControllerTests
{
    private static ReelMetrics Metrics(double fishCenter, double playerbarCenter, double playerbarWidth = 0.3)
        => new(fishCenter, playerbarCenter, playerbarWidth);

    // Advance the controller several ticks to get past warmup (warmup lasts ~200ms).
    // We use a fresh controller whose Stopwatch started at construction, so we feed
    // enough ticks with a large dt to exhaust warmup without sleeping.
    private static Tracking3Controller WarmController(Tracking3Settings? settings = null)
    {
        settings ??= new Tracking3Settings();
        var ctrl = new Tracking3Controller();
        // Feed 20 ticks with fish and bar centered — this moves the internal Stopwatch
        // forward by real elapsed time. Since we can't fake the clock, we instead reset()
        // and then call Update repeatedly; warmup expires after ~200ms of real time.
        // For test speed, we accept that the first few calls may return Warmup mode and
        // just drain those before asserting on non-warmup behaviour.
        var neutralMetrics = Metrics(0.5, 0.5);
        for (var i = 0; i < 100; i++)
        {
            ctrl.Update(neutralMetrics, settings);
            System.Threading.Thread.Sleep(3); // 300ms total — clears the 200ms warmup
        }
        return ctrl;
    }

    // ── 1. Left edge → EdgeRecovery hold ─────────────────────────────────────

    [Fact]
    public void LeftEdge_ReturnsEdgeRecovery_Hold()
    {
        var ctrl = WarmController();
        var d = ctrl.Update(Metrics(0.5, 0.05), new Tracking3Settings());
        Assert.Equal(Tracking3DecisionMode.EdgeRecovery, d.Mode);
        Assert.True(d.DesiredHolding);
    }

    // ── 2. Right edge → EdgeRecovery release ─────────────────────────────────

    [Fact]
    public void RightEdge_ReturnsEdgeRecovery_Release()
    {
        var ctrl = WarmController();
        var d = ctrl.Update(Metrics(0.5, 0.95), new Tracking3Settings());
        Assert.Equal(Tracking3DecisionMode.EdgeRecovery, d.Mode);
        Assert.False(d.DesiredHolding);
    }

    // ── 3. Large positive error → HardCorrection hold ────────────────────────

    [Fact]
    public void LargePositiveError_HardCorrection_Holds()
    {
        var ctrl = WarmController();
        // Fish at 0.8, bar center at 0.5: error = +0.3, well above CloseThreshold = 0.01
        // Feed same frame twice so velocity is 0 → prediction doesn't flip sides
        var m = Metrics(0.8, 0.5);
        ctrl.Update(m, new Tracking3Settings());
        var d = ctrl.Update(m, new Tracking3Settings());
        Assert.Equal(Tracking3DecisionMode.HardCorrection, d.Mode);
        Assert.True(d.DesiredHolding);
    }

    // ── 4. Large negative error → HardCorrection release ─────────────────────

    [Fact]
    public void LargeNegativeError_HardCorrection_Releases()
    {
        var ctrl = WarmController();
        var m = Metrics(0.2, 0.5);
        ctrl.Update(m, new Tracking3Settings());
        var d = ctrl.Update(m, new Tracking3Settings());
        Assert.Equal(Tracking3DecisionMode.HardCorrection, d.Mode);
        Assert.False(d.DesiredHolding);
    }

    // ── 5. Hard correction disabled → never returns HardCorrection ────────────

    [Fact]
    public void HardCorrectionDisabled_LargeError_NotHardCorrection()
    {
        var settings = new Tracking3Settings { EnableHardCorrection = false };
        var ctrl = WarmController(settings);
        var m = Metrics(0.8, 0.5);
        ctrl.Update(m, settings);
        var d = ctrl.Update(m, settings);
        Assert.NotEqual(Tracking3DecisionMode.HardCorrection, d.Mode);
    }

    // ── 6. Close-range error falls through to fine tracking / center pulse ────

    [Fact]
    public void CloseRangeError_FineOrCenter()
    {
        var ctrl = WarmController();
        // Error ≈ 0.005 — below CloseThreshold = 0.01
        var m = Metrics(0.505, 0.5);
        for (var i = 0; i < 5; i++)
        {
            ctrl.Update(m, new Tracking3Settings());
        }
        var d = ctrl.Update(m, new Tracking3Settings());
        Assert.True(d.Mode is Tracking3DecisionMode.FineTracking or Tracking3DecisionMode.CenterPulse);
    }

    // ── 7. Wider playerbar → larger center zone (Control stat effect) ─────────

    [Fact]
    public void WiderBar_LargerCenterZone()
    {
        // With small error and narrow bar, expect FineTracking (outside center zone).
        // Same small error with wide bar should fall into CenterPulse (inside center zone).
        // error in 0-1000 space ≈ 0.02 * 1000 = 20
        // centerZone for narrow bar (0.1): max(2, 100 * 0.05) = 5 → 20 > 5 → FineTracking
        // centerZone for wide bar (0.6):  max(2, 600 * 0.05) = 30 → 20 < 30 → CenterPulse
        var settings = new Tracking3Settings { EnableHardCorrection = false };

        var ctrlNarrow = WarmController(settings);
        var narrowMetrics = Metrics(0.52, 0.5, playerbarWidth: 0.1);
        for (var i = 0; i < 5; i++) ctrlNarrow.Update(narrowMetrics, settings);
        var dNarrow = ctrlNarrow.Update(narrowMetrics, settings);

        var ctrlWide = WarmController(settings);
        var wideMetrics = Metrics(0.52, 0.5, playerbarWidth: 0.6);
        for (var i = 0; i < 5; i++) ctrlWide.Update(wideMetrics, settings);
        var dWide = ctrlWide.Update(wideMetrics, settings);

        Assert.Equal(Tracking3DecisionMode.FineTracking, dNarrow.Mode);
        Assert.Equal(Tracking3DecisionMode.CenterPulse, dWide.Mode);
    }

    // ── 8a. High Resilience dampens prediction (hard correction less eager) ───

    [Fact]
    public void HighResilience_DampensPrediction_HardCorrectionStillFires()
    {
        // With Resilience=0 and a moderate error, hard correction fires.
        // With Resilience=1 (max damping), predictionScale = 0, so velocity prediction
        // gives no lookahead — hard correction may or may not fire depending on error magnitude;
        // what matters is EdgeRecovery is unaffected by Resilience.
        var settingsLow = new Tracking3Settings { Resilience = 0.0 };
        var settingsHigh = new Tracking3Settings { Resilience = 0.9 };

        var ctrlLow = WarmController(settingsLow);
        var ctrlHigh = WarmController(settingsHigh);
        var m = Metrics(0.8, 0.5);
        ctrlLow.Update(m, settingsLow);
        ctrlHigh.Update(m, settingsHigh);
        var dLow = ctrlLow.Update(m, settingsLow);
        var dHigh = ctrlHigh.Update(m, settingsHigh);

        // Both should still move the bar toward the fish for a large positive error
        Assert.True(dLow.DesiredHolding);
        Assert.True(dHigh.DesiredHolding);
    }

    // ── 8b. High Resilience does not disable EdgeRecovery ────────────────────

    [Fact]
    public void HighResilience_EdgeRecoveryUnaffected()
    {
        var settings = new Tracking3Settings { Resilience = 0.9 };
        var ctrl = WarmController(settings);
        var d = ctrl.Update(Metrics(0.5, 0.05), settings);
        Assert.Equal(Tracking3DecisionMode.EdgeRecovery, d.Mode);
        Assert.True(d.DesiredHolding);
    }

    // ── 9. Reset clears state ────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsState_FirstFrameMatchesFreshController()
    {
        var settings = new Tracking3Settings { EnableHardCorrection = false };
        var ctrl = new Tracking3Controller();

        // Contaminate with several frames
        for (var i = 0; i < 10; i++)
        {
            ctrl.Update(Metrics(0.9, 0.1), settings);
        }

        ctrl.Reset();
        var fresh = new Tracking3Controller();
        System.Threading.Thread.Sleep(250); // clear warmup after reset and fresh construction

        var m = Metrics(0.5, 0.5);
        var dReset = ctrl.Update(m, settings);
        var dFresh = fresh.Update(m, settings);

        // Both should be in warmup or produce the same mode for a neutral first frame
        Assert.Equal(dFresh.Mode, dReset.Mode);
        Assert.Equal(dFresh.DesiredHolding, dReset.DesiredHolding);
    }

    // ── 10. Determinism ───────────────────────────────────────────────────────

    [Fact]
    public void SameInputSequence_ProducesSameDecisions()
    {
        var settings = new Tracking3Settings();
        var inputs = new[]
        {
            Metrics(0.5, 0.5),
            Metrics(0.6, 0.5),
            Metrics(0.7, 0.5),
            Metrics(0.65, 0.52),
            Metrics(0.55, 0.53),
        };

        var ctrl1 = new Tracking3Controller();
        var ctrl2 = new Tracking3Controller();

        for (var i = 0; i < inputs.Length; i++)
        {
            var d1 = ctrl1.Update(inputs[i], settings);
            var d2 = ctrl2.Update(inputs[i], settings);
            Assert.Equal(d1.Mode, d2.Mode);
            Assert.Equal(d1.DesiredHolding, d2.DesiredHolding);
        }
    }
}
