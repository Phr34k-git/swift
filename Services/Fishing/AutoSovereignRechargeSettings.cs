namespace Client.Services.Fishing;

public static class AutoSovereignRechargeSettings
{
    public static bool Enabled { get; set; }
    public static bool RuntimeBusy { get; set; }
    public static bool PauseFishingRequested { get; set; }
    public static bool PendingRequested { get; set; }
    public static bool SequenceStartRequested { get; set; }

    public static double MinimumPercent { get; set; } = 95.0;

    public static double MaximumPercent { get; set; } = 99.0;
}
