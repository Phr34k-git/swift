namespace Client.Services.Fishing;

public static class AutoTotemSettings
{
    public static bool Enabled { get; set; }

    public static string TotemName { get; set; } = "None";

    public static AutoTotemSpecial Special { get; set; } = AutoTotemSpecial.None;

    public static AutoTotemTimePreference TimePreference { get; set; } = AutoTotemTimePreference.None;

    public static AutoTotemMode Mode { get; set; } = AutoTotemMode.Expire;

    public static int IntervalSeconds { get; set; } = 900;

    public static int UseSettleDelayMs { get; set; } = 200;

    public static int TimeChangeWaitMs { get; set; } = 1300;

    public static int MaxSundialAttempts { get; set; } = 4;
}

public enum AutoTotemSpecial
{
    None,
    Shiny,
    Sparkling,
    Mutation,
}

public enum AutoTotemTimePreference
{
    None,
    Day,
    Night,
}

