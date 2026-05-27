using System;
using System.Diagnostics;

namespace Client.Services.Fishing;

internal sealed class Tracking3Controller
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    // Shared velocity tracking (raw frame deltas, Tracking2-style)
    private double? _lastPlayerbarCenter;
    private double? _lastFishPos;

    // Fine-tracking state (Tracking1-style, scaled to 0-1000 space)
    private bool _hasLastFrame;
    private double _lastTime;
    private double _prevFishX;
    private double _prevBarCenter;
    private double _fishVelocityEma;
    private double _barVelocityEma;
    private double _smoothFishX;
    private double _smoothBarCenter;
    private double _smoothControl;
    private double _errorIntegral;
    private double _centerPulseReleaseUntil;
    private bool _wasInStableZone;
    private double _stableHybridUntil;

    // Warmup
    private double _trackingWarmupUntil;

    public Tracking3Decision Update(ReelMetrics metrics, Tracking3Settings settings)
    {
        // PlayerbarCenter from ReelMetrics is the center of the playerbar.
        var fishPos = metrics.FishCenter;
        var playerbarCenter = metrics.PlayerbarCenter;
        var playerbarWidth = metrics.PlayerbarWidth;

        // --- Raw frame velocities (Tracking2-style) ---
        _lastPlayerbarCenter ??= playerbarCenter;
        _lastFishPos ??= fishPos;
        var playerbarVelocity = playerbarCenter - _lastPlayerbarCenter.Value;
        _lastPlayerbarCenter = playerbarCenter;
        _lastFishPos = fishPos;

        var error = fishPos - playerbarCenter;

        // 1. Warmup
        var now = _stopwatch.Elapsed.TotalSeconds;
        if (now < _trackingWarmupUntil)
        {
            UpdateFineState(metrics, settings, now);
            var warmupDeadzone = Math.Max(0.015, playerbarWidth * 0.04);
            bool warmupHold;
            if (error > warmupDeadzone)
            {
                warmupHold = true;
            }
            else if (error < -warmupDeadzone)
            {
                warmupHold = false;
            }
            else
            {
                warmupHold = false;
            }
            return new Tracking3Decision(error, warmupHold ? 1.0 : 0.0, warmupHold, Tracking3DecisionMode.Warmup);
        }

        // 2. Edge recovery — uses playerbar center against boundary
        if (playerbarCenter < settings.EdgeBoundary)
        {
            UpdateFineState(metrics, settings, now);
            return new Tracking3Decision(error, 1.0, true, Tracking3DecisionMode.EdgeRecovery);
        }
        if (playerbarCenter > 1.0 - settings.EdgeBoundary)
        {
            UpdateFineState(metrics, settings, now);
            return new Tracking3Decision(error, 0.0, false, Tracking3DecisionMode.EdgeRecovery);
        }

        // 3. Hard correction
        if (settings.EnableHardCorrection && Math.Abs(error) > settings.CloseThreshold)
        {
            var predictionScale = settings.PredictionStrength * (1.0 - settings.Resilience);
            var predicted = playerbarCenter + playerbarVelocity * predictionScale;
            var predictedError = fishPos - predicted;
            var sameSideAfterPrediction = error * predictedError > 0;
            var approachingTarget = error * playerbarVelocity > 0;
            var remainingDistance = Math.Max(0.0, Math.Abs(error) - settings.CloseThreshold);
            var brakeLookahead = Math.Abs(playerbarVelocity) * 8.0;
            var needsPreSlow = approachingTarget && brakeLookahead >= remainingDistance;

            if (sameSideAfterPrediction && !needsPreSlow)
            {
                UpdateFineState(metrics, settings, now);
                var hardHold = error > 0;
                return new Tracking3Decision(error, hardHold ? 1.0 : 0.0, hardHold, Tracking3DecisionMode.HardCorrection);
            }
        }

        // 4 & 5. Fine tracking + center pulse (Tracking1 brain)
        return ComputeFine(metrics, settings, now, error);
    }

    public void Reset()
    {
        _stopwatch.Restart();
        _lastPlayerbarCenter = null;
        _lastFishPos = null;
        _hasLastFrame = false;
        _lastTime = 0;
        _prevFishX = 0;
        _prevBarCenter = 0;
        _fishVelocityEma = 0;
        _barVelocityEma = 0;
        _smoothFishX = 0;
        _smoothBarCenter = 0;
        _smoothControl = 0;
        _errorIntegral = 0;
        _centerPulseReleaseUntil = 0;
        _wasInStableZone = false;
        _stableHybridUntil = 0;
        _trackingWarmupUntil = _stopwatch.Elapsed.TotalSeconds + 0.2;
    }

    // Updates fine-tracking EMA state without computing a decision (keeps state warm during edge/hard branches).
    private void UpdateFineState(ReelMetrics metrics, Tracking3Settings settings, double now)
    {
        var fishX = metrics.FishCenter * 1000.0;
        var barCenter = metrics.PlayerbarCenter * 1000.0;
        var dt = _hasLastFrame ? Math.Max(0.001, now - _lastTime) : 0.016;

        if (!_hasLastFrame)
        {
            _smoothFishX = fishX;
            _smoothBarCenter = barCenter;
            _prevFishX = fishX;
            _prevBarCenter = barCenter;
            _lastTime = now;
            _hasLastFrame = true;
            return;
        }

        var alpha = Clamp(settings.PositionAlpha, 0.2, 0.92);
        _smoothFishX = alpha * fishX + (1.0 - alpha) * _smoothFishX;
        _smoothBarCenter = alpha * barCenter + (1.0 - alpha) * _smoothBarCenter;

        var fishVelocity = (_smoothFishX - _prevFishX) / dt;
        var barVelocity = (_smoothBarCenter - _prevBarCenter) / dt;
        _fishVelocityEma = settings.FishVelAlpha * fishVelocity + (1.0 - settings.FishVelAlpha) * _fishVelocityEma;
        _barVelocityEma = settings.BarVelAlpha * barVelocity + (1.0 - settings.BarVelAlpha) * _barVelocityEma;
        var measuredMaxVelocity = Math.Max(1.0, settings.MaxVelocity * 1000.0);
        _barVelocityEma = Clamp(_barVelocityEma, -measuredMaxVelocity, measuredMaxVelocity);

        _prevFishX = _smoothFishX;
        _prevBarCenter = _smoothBarCenter;
        _lastTime = now;
    }

    private Tracking3Decision ComputeFine(ReelMetrics metrics, Tracking3Settings settings, double now, double rawError)
    {
        var fishX = metrics.FishCenter * 1000.0;
        var barCenter = metrics.PlayerbarCenter * 1000.0;
        var barWidth = Math.Max(1.0, metrics.PlayerbarWidth * 1000.0);
        var dt = _hasLastFrame ? Math.Max(0.001, now - _lastTime) : 0.016;

        if (!_hasLastFrame)
        {
            _smoothFishX = fishX;
            _smoothBarCenter = barCenter;
            _prevFishX = fishX;
            _prevBarCenter = barCenter;
            _lastTime = now;
            _hasLastFrame = true;
        }
        else
        {
            var alpha = Clamp(settings.PositionAlpha, 0.2, 0.92);
            _smoothFishX = alpha * fishX + (1.0 - alpha) * _smoothFishX;
            _smoothBarCenter = alpha * barCenter + (1.0 - alpha) * _smoothBarCenter;
        }

        var fishVelocityRaw = (_smoothFishX - _prevFishX) / dt;
        var barVelocityRaw = (_smoothBarCenter - _prevBarCenter) / dt;
        _fishVelocityEma = settings.FishVelAlpha * fishVelocityRaw + (1.0 - settings.FishVelAlpha) * _fishVelocityEma;
        _barVelocityEma = settings.BarVelAlpha * barVelocityRaw + (1.0 - settings.BarVelAlpha) * _barVelocityEma;
        var measuredMaxVelocity = Math.Max(1.0, settings.MaxVelocity * 1000.0);
        _barVelocityEma = Clamp(_barVelocityEma, -measuredMaxVelocity, measuredMaxVelocity);

        _prevFishX = _smoothFishX;
        _prevBarCenter = _smoothBarCenter;
        _lastTime = now;

        // Compute error in scaled space
        double error;
        if (settings.UsePrediction)
        {
            var baseError = _smoothFishX - _smoothBarCenter;
            var fishLead = Clamp(_fishVelocityEma * settings.FishPredT, -Math.Max(2.0, barWidth * 0.18), Math.Max(2.0, barWidth * 0.18));
            var holdingForPred = _smoothControl > 0;
            var measuredAcceleration = (holdingForPred ? settings.HoldAcceleration : settings.ReleaseAcceleration) * 1000.0;
            var predictedBarVelocity = Clamp(_barVelocityEma + measuredAcceleration * settings.BarPredT, -measuredMaxVelocity, measuredMaxVelocity);
            var barLead = Clamp(predictedBarVelocity * settings.BarPredT, -Math.Max(2.0, barWidth * 0.12), Math.Max(2.0, barWidth * 0.12));
            var predictedError = _smoothFishX + 0.25 * fishLead - (_smoothBarCenter + 0.175 * barLead);
            error = 0.65 * baseError + 0.35 * predictedError;
        }
        else
        {
            error = _smoothFishX - _smoothBarCenter;
        }

        if (_fishVelocityEma > 0 && error > -barWidth * 0.1)
        {
            error += Clamp(_fishVelocityEma * settings.RightMoveLeadT, 0, Math.Max(2.0, barWidth * 0.22));
        }

        error += Clamp(barWidth * 0.035, 1.5, 5.0);
        _errorIntegral = Clamp(_errorIntegral + error * dt, -settings.IntegralClamp, settings.IntegralClamp);

        var sideMargin = barWidth * settings.BarRatioFromSide;
        var clamp = Math.Max(settings.PdClamp > 0 ? settings.PdClamp : 30.0, settings.OnThreshold + 1.0);
        double rawControl;
        if (_smoothFishX < sideMargin)
        {
            rawControl = -clamp;
        }
        else if (_smoothFishX > 1000.0 - sideMargin)
        {
            rawControl = clamp;
        }
        else
        {
            var relativeVelocity = _fishVelocityEma - _barVelocityEma;
            rawControl = settings.Kp * error + settings.Ki * _errorIntegral + settings.Kd * relativeVelocity;
            if (settings.PdClamp > 0)
            {
                rawControl = Clamp(rawControl, -settings.PdClamp, settings.PdClamp);
            }
        }

        var control = _smoothControl = settings.ControlAlpha * rawControl + (1.0 - settings.ControlAlpha) * _smoothControl;

        // Center zone: CenterPulse; outside: FineTracking
        var centerZone = Math.Max(2.0, barWidth * settings.CenterZoneRatio);
        bool desiredHolding;
        Tracking3DecisionMode mode;

        if (Math.Abs(error) <= centerZone)
        {
            var enteringStable = !_wasInStableZone;
            _wasInStableZone = true;
            if (enteringStable)
            {
                _stableHybridUntil = now + 3.0;
            }

            if (now < _stableHybridUntil)
            {
                // Temporary hybrid behavior while entering stable zone.
                mode = Tracking3DecisionMode.FineTracking;
                if (control > settings.OnThreshold)
                {
                    desiredHolding = true;
                }
                else if (control < -settings.OnThreshold)
                {
                    desiredHolding = false;
                }
                else
                {
                    desiredHolding = Math.Abs(control) >= settings.OffThreshold && false;
                }
            }
            else
            {
                mode = Tracking3DecisionMode.CenterPulse;
                if (now < _centerPulseReleaseUntil)
                {
                    desiredHolding = false;
                }
                else if (control > settings.OnThreshold)
                {
                    var hold = PositiveModulo(now, settings.CenterPulsePeriodS) < Math.Max(0.001, settings.CenterPulseHoldS);
                    if (hold)
                    {
                        _centerPulseReleaseUntil = now + Math.Max(0, settings.CenterReleaseBlipS);
                    }
                    desiredHolding = hold;
                }
                else if (control < -settings.OnThreshold)
                {
                    desiredHolding = false;
                }
                else
                {
                    desiredHolding = control > 0 && PositiveModulo(now, settings.CenterWeakPeriodS) < Math.Max(0.001, settings.CenterWeakHoldS);
                }
            }
        }
        else
        {
            _wasInStableZone = false;
            mode = Tracking3DecisionMode.FineTracking;
            if (control > settings.OnThreshold)
            {
                desiredHolding = true;
            }
            else if (control < -settings.OnThreshold)
            {
                desiredHolding = false;
            }
            else
            {
                desiredHolding = Math.Abs(control) >= settings.OffThreshold && false;
            }
        }

        return new Tracking3Decision(rawError, control, desiredHolding, mode);
    }

    private static double PositiveModulo(double value, double modulus)
    {
        modulus = Math.Max(0.001, modulus);
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static double Clamp(double value, double min, double max)
    {
        return value < min ? min : value > max ? max : value;
    }
}
