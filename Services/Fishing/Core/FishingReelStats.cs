namespace Client.Services.Fishing;

public enum FishingReelOutcome
{
    None,
    Caught,
    Lost,
    Skipped,
}

public readonly record struct FishingReelStats(int Caught, int Lost)
{
    public static FishingReelStats Empty { get; } = new(0, 0);

    public int Total => Caught + Lost;

    public double SuccessRatePercent => Total > 0 ? Caught / (double)Total * 100.0 : 0.0;

    public FishingReelStats Record(FishingReelOutcome outcome)
    {
        return outcome switch
        {
            FishingReelOutcome.Caught => this with { Caught = Caught + 1 },
            FishingReelOutcome.Lost => this with { Lost = Lost + 1 },
            _ => this,
        };
    }
}
