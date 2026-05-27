namespace Client.Services.Fishing;

internal enum Tracking3DecisionMode
{
    Warmup,
    EdgeRecovery,
    HardCorrection,
    FineTracking,
    CenterPulse,
}

internal sealed record Tracking3Decision(
    double Error,
    double Control,
    bool DesiredHolding,
    Tracking3DecisionMode Mode);
