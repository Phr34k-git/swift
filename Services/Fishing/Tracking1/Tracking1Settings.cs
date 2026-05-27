namespace Client.Services.Fishing;

public sealed class Tracking1Settings
{
    public double Kp { get; init; } = 1.25;
    public double Ki { get; init; } = 0.06;
    public double Kd { get; init; } = 1.85;
    public double PdClamp { get; init; } = 34.0;
    public double IntegralClamp { get; init; } = 130.0;
    public double BarRatioFromSide { get; init; } = 0.6;
    public double CenterZoneRatio { get; init; } = 0.05;
    public double CenterPulsePeriodS { get; init; } = 0.024;
    public double CenterPulseHoldS { get; init; } = 0.1;
    public double CenterReleaseBlipS { get; init; } = 0.006;
    public double CenterWeakPeriodS { get; init; } = 0.026;
    public double CenterWeakHoldS { get; init; } = 0.006;
    public double OnThreshold { get; init; } = 7.0;
    public double OffThreshold { get; init; } = 3.5;
    public bool UsePrediction { get; init; } = true;
    public double FishPredT { get; init; } = 0.165;
    public double BarPredT { get; init; } = 0.028;
    public double RightMoveLeadT { get; init; } = 0.085;
    public double HoldAcceleration { get; init; } = 0.467224;
    public double ReleaseAcceleration { get; init; } = -0.205181;
    public double MaxVelocity { get; init; } = 0.829134;
    public double DampingFriction { get; init; } = 43.726384;
    public double FishVolatility { get; init; } = 26.829498;
    public double ProgressGainRate { get; init; } = 13.803304;
    public double ProgressLossRate { get; init; } = 13.682059;
    public double EffectiveSampleRateHz { get; init; } = 250.06541;
    public double FishVelAlpha { get; init; } = 0.82;
    public double BarVelAlpha { get; init; } = 0.78;
    public double PositionAlpha { get; init; } = 0.9;
    public double ControlAlpha { get; init; } = 0.94;
    public double TickMs { get; init; } = 20;
    public double ProbeDelaySeconds { get; init; } = 0.25;
    public double CompletionThreshold { get; init; } = 99.5;
}