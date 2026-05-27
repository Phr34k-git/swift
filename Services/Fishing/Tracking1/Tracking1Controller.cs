using System;
using System.Diagnostics;

namespace Client.Services.Fishing;

internal sealed class Tracking1Controller
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _holding;
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
    private bool _casting;
    private bool _castHolding;
    private double _nextCastChangeTime;

    public bool IsHolding => _holding;

    public void SetPredictionWarmupSeconds(double seconds)
    {
        // Kept for API compatibility; Predict no longer uses warmup.
    }

    public ReelDecision UpdateTracking(ReelMetrics metrics, Tracking1Settings settings)
    {
        if (_casting)
        {
            _casting = false;
            _castHolding = false;
            _nextCastChangeTime = 0;
            Release();
            ResetTrackingState();
        }

        var state = Compute(metrics, settings);
        SetHold(state.Holding);
        return state;
    }

    public ReelDecision UpdateCasting()
    {
        var now = _stopwatch.Elapsed.TotalSeconds;
        if (!_casting)
        {
            ResetTrackingState();
            _casting = true;
            _castHolding = false;
            _nextCastChangeTime = 0;
        }

        if (now >= _nextCastChangeTime)
        {
            _castHolding = !_castHolding;
            SetHold(_castHolding);
            _nextCastChangeTime = now + 0.2;
        }

        return new ReelDecision(0, 0, _castHolding);
    }

    public void Reset()
    {
        Release();
        _casting = false;
        _castHolding = false;
        _nextCastChangeTime = 0;
        ResetTrackingState();
    }

    public void Release()
    {
        SetHold(false);
    }

    private void ResetTrackingState()
    {
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
    }

    private ReelDecision Compute(ReelMetrics metrics, Tracking1Settings settings)
    {
        var now = _stopwatch.Elapsed.TotalSeconds;
        var dt = _hasLastFrame ? Math.Max(0.001, now - _lastTime) : 0.016;
        var fishX = metrics.FishCenter * 1000.0;
        var barCenter = metrics.PlayerbarCenter * 1000.0;
        var barWidth = Math.Max(1.0, metrics.PlayerbarWidth * 1000.0);

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

        var fishVelocity = (_smoothFishX - _prevFishX) / dt;
        var barVelocity = (_smoothBarCenter - _prevBarCenter) / dt;
        _fishVelocityEma = settings.FishVelAlpha * fishVelocity + (1.0 - settings.FishVelAlpha) * _fishVelocityEma;
        _barVelocityEma = settings.BarVelAlpha * barVelocity + (1.0 - settings.BarVelAlpha) * _barVelocityEma;
        _prevFishX = _smoothFishX;
        _prevBarCenter = _smoothBarCenter;
        _lastTime = now;

        double error;
        if (settings.UsePrediction)
        {
            var baseError = _smoothFishX - _smoothBarCenter;
            var fishLead = Clamp(_fishVelocityEma * settings.FishPredT, -Math.Max(2.0, barWidth * 0.18), Math.Max(2.0, barWidth * 0.18));
            var barLead = Clamp(_barVelocityEma * settings.BarPredT, -Math.Max(2.0, barWidth * 0.12), Math.Max(2.0, barWidth * 0.12));
            var predictedError = _smoothFishX + 0.5 * fishLead - (_smoothBarCenter + 0.35 * barLead);
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
        var fishScaled = _smoothFishX;
        var clamp = Math.Max(settings.PdClamp > 0 ? settings.PdClamp : 30.0, settings.OnThreshold + 1.0);
        double rawControl;
        if (fishScaled < sideMargin)
        {
            rawControl = -clamp;
        }
        else if (fishScaled > 1000.0 - sideMargin)
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
        return new ReelDecision(error, control, DecideHold(settings, now, error, control, barWidth));
    }

    private bool DecideHold(Tracking1Settings settings, double now, double error, double control, double boxLen)
    {
        var centerZone = Math.Max(2.0, boxLen * settings.CenterZoneRatio);
        if (Math.Abs(error) <= centerZone)
        {
            if (now < _centerPulseReleaseUntil)
            {
                return false;
            }

            if (control > settings.OnThreshold)
            {
                var hold = PositiveModulo(now, settings.CenterPulsePeriodS) < Math.Max(0.001, settings.CenterPulseHoldS);
                if (hold)
                {
                    _centerPulseReleaseUntil = now + Math.Max(0, settings.CenterReleaseBlipS);
                }

                return hold;
            }

            if (control < -settings.OnThreshold)
            {
                return false;
            }

            if (control > 0)
            {
                return PositiveModulo(now, settings.CenterWeakPeriodS) < Math.Max(0.001, settings.CenterWeakHoldS);
            }

            return false;
        }

        if (control > settings.OnThreshold)
        {
            return true;
        }

        if (control < -settings.OnThreshold)
        {
            return false;
        }

        if (Math.Abs(control) < settings.OffThreshold)
        {
            return false;
        }

        return false;
    }

    private void SetHold(bool value)
    {
        if (value == _holding)
        {
            return;
        }

        if (value)
        {
            NativeMouse.LeftDown();
        }
        else
        {
            NativeMouse.LeftUp();
        }

        _holding = value;
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

internal sealed record ReelDecision(double Error, double Control, bool Holding);
