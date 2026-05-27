using System;

namespace Client.Services.Fishing;

// Pure PWM tracking controller — computes a hold decision but does not actuate
// the mouse. The tracker applies the decision through its gated actuator.
internal sealed class Tracking2Controller
{
    private double? _lastPlayerbarPos;
    private double? _lastFishPos;
    private double _pwmAccumulator;

    public ReelDecision Update(ReelMetrics metrics, Tracking2Settings settings)
    {
        var fishPos = metrics.FishCenter;
        var playerbarPos = metrics.PlayerbarCenter;

        _lastPlayerbarPos ??= playerbarPos;
        _lastFishPos ??= fishPos;

        var playerbarVelocity = playerbarPos - _lastPlayerbarPos.Value;
        var fishVelocity = fishPos - _lastFishPos.Value;
        _lastPlayerbarPos = playerbarPos;
        _lastFishPos = fishPos;

        var error = fishPos - playerbarPos;
        if (playerbarPos < settings.EdgeBoundary)
        {
            return new ReelDecision(error, 1, true);
        }

        if (playerbarPos > 1.0 - settings.EdgeBoundary)
        {
            return new ReelDecision(error, 0, false);
        }

        var predictionScale = settings.PredictionStrength * (1.0 - settings.Resilience);
        var predicted = playerbarPos + playerbarVelocity * predictionScale;
        var predictedError = fishPos - predicted;
        var sameSideAfterPrediction = error * predictedError > 0;
        var approachingTarget = error * playerbarVelocity > 0;
        var remainingDistance = Math.Max(0.0, Math.Abs(error) - settings.CloseThreshold);
        var brakeLookahead = Math.Abs(playerbarVelocity) * 8.0;
        var needsPreSlow = approachingTarget && brakeLookahead >= remainingDistance;

        if (Math.Abs(error) > settings.CloseThreshold && sameSideAfterPrediction && !needsPreSlow)
        {
            return error > 0
                ? new ReelDecision(error, 1, true)
                : new ReelDecision(error, 0, false);
        }

        double targetDuty;
        if (needsPreSlow && brakeLookahead > 0)
        {
            var brakeUrgency = 1.0 - Math.Min(1.0, remainingDistance / brakeLookahead);
            targetDuty = error > 0
                ? settings.NeutralDutyCycle * (1.0 - brakeUrgency)
                : settings.NeutralDutyCycle + (1.0 - settings.NeutralDutyCycle) * brakeUrgency;
        }
        else
        {
            var adjustment = settings.ProportionalGain * error +
                settings.DerivativeGain * fishVelocity -
                settings.VelocityDamping * playerbarVelocity;
            targetDuty = Clamp(settings.NeutralDutyCycle + adjustment, 0, 1);
        }

        _pwmAccumulator += targetDuty;
        if (_pwmAccumulator >= 1.0)
        {
            _pwmAccumulator -= 1.0;
            return new ReelDecision(error, targetDuty, true);
        }

        return new ReelDecision(error, targetDuty, false);
    }

    public void Reset()
    {
        _lastPlayerbarPos = null;
        _lastFishPos = null;
        _pwmAccumulator = 0;
    }

    private static double Clamp(double value, double min, double max)
    {
        return value < min ? min : value > max ? max : value;
    }
}
