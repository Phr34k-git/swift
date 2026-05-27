using System.Collections.Generic;

namespace Client.Services.Fishing;

internal static class EnchantCatalog
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Abyssal",
        "Blessed",
        "Blood Reckoning",
        "Breezed",
        "Chaotic",
        "Chronos",
        "Clever",
        "Controlled",
        "Divine",
        "Flashline",
        "Ghastly",
        "Hasty",
        "Hunter",
        "Insight",
        "Long",
        "Lucky",
        "Momentum",
        "Mutated",
        "Noir",
        "Quality",
        "Resilient",
        "Scavenger",
        "Sea King",
        "Scrapper",
        "Steady",
        "Storming",
        "Swift",
        "Unbreakable",
        "Wormhole",
    ];
}
