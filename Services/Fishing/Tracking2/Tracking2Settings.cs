namespace Client.Services.Fishing;

public sealed class Tracking2Settings
{
    public double CloseThreshold { get; init; } = 0.01;
    public double DerivativeGain { get; init; } = 0.55;
    public double EdgeBoundary { get; init; } = 0.1;
    public double NeutralDutyCycle { get; init; } = 0.5;
    public double PredictionStrength { get; init; } = 7.5;
    public double ProportionalGain { get; init; } = 0.42;
    public double Resilience { get; init; } = 0.0;
    public int UpdateRateMs { get; init; } = 21;
    public double VelocityDamping { get; init; } = 38;
    public string CastMode { get; init; } = "perfect";
    public double CastPowerCustom { get; init; } = 96.0;
    public int CastTimeoutMs { get; init; } = 15000;
    public int PreCastDelayMs { get; init; } = 0;
    public int PostCastDelayMs { get; init; } = 300;
    public bool CastOnTimeout { get; init; } = true;
    public int FishingActionDelayMs { get; init; } = 0;
    public double CompletionThreshold { get; init; } = 99.5;
    public int ShakeIntervalMs { get; init; } = 25;
}
