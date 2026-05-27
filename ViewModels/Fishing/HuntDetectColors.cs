using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Client.ViewModels;

public static class HuntDetectColors
{
    private static readonly string[] UiDisplayNames =
    [
        "Sovereign Surge",
        "Sovereign Storm",
        "Sovereign Reckoning",
        "Wisp Haunt",
        "Soul Scourge",
        "Styx Angler",
        "Plesiosaur",
        "Pliosaur",
        "Ancestral Pliosaur",
        "Reef Titan",
        "Colossus Reef Titan",
        "Omnithal",
        "Awakened Omnithal",
        "Goldwraith",
        "Ancient Goldwraith",
        "Storm Flood",
        "Tidecrasher Archon",
        "Megalodon",
        "Ancient Megalodon",
        "Phantom Megalodon",
        "Solar Chorus",
        "Helios Sunray",
        "Kerauno Wyrm",
        "War Surge",
        "Legionnaire Lamprey",
        "Kraken",
        "Ancient Kraken",
        "Orca Migration",
        "Whale Migration",
        "Great White Shark",
        "Great Hammerhead Shark",
        "Whale Shark",
        "Leviathan",
        "Profane Leviathan",
        "Skeletal Leviathan",
        "Beluga",
        "Narwhal",
        "Magician Narwhal",
        "Mosslurker",
        "Dreadfin",
        "Scylla",
        "Mossjaw",
        "Elder Mossjaw",
        "Flower Guardian",
        "Toxic Guardian",
        "Rotbloom",
        "Ashclaw",
        "Bloop Fish",
        "Frostwyrm",
        "Wyvern",
        "Earthquake",
        "Sunken Chests",
        "Humpback Whale",
        "Megamouth Shark",
        "Baby Bloop Fish",
        "Colossal Ancient Dragon",
        "Colossal Blue Dragon",
        "Colossal Ethereal Dragon",
        "Olympian Devil",
    ];

    private static readonly Color DefaultColor = Color.FromArgb(255, 172, 178, 186);
    private static readonly IBrush DefaultBrush = new SolidColorBrush(DefaultColor);
    private static readonly Color OlympianDevilEmbedColor = Color.FromArgb(255, 213, 123, 153);
    private static readonly Color ColossalBlueDragonEmbedColor = Color.FromArgb(255, 80, 200, 255);
    private static readonly Color ColossalEtherealDragonEmbedColor = Color.FromArgb(255, 243, 198, 255);
    private static readonly IBrush OlympianDevilGradientBrush = CreateGradientBrush(
        Color.FromArgb(255, 182, 139, 231),
        Color.FromArgb(255, 213, 123, 153),
        Color.FromArgb(255, 245, 159, 76));
    private static readonly IBrush ColossalEtherealDragonGradientBrush = CreateGradientBrush(
        Color.FromArgb(255, 230, 140, 255),
        Color.FromArgb(255, 243, 198, 255),
        Color.FromArgb(255, 255, 255, 255));

    private static readonly IReadOnlyDictionary<string, Color> Colors =
        new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sovereign Surge"] = Rgb(50, 160, 255),
            ["Sovereign Storm"] = Rgb(100, 90, 255),
            ["Sovereign Reckoning"] = Rgb(180, 70, 255),
            ["Wisp Haunt"] = Rgb(120, 255, 170),
            ["Soul Scourge"] = Rgb(180, 120, 70),
            ["Styx Angler"] = Rgb(120, 170, 255),
            ["Plesiosaur"] = Rgb(120, 220, 255),
            ["Pliosaur"] = Rgb(255, 220, 120),
            ["Ancestral Pliosaur"] = Rgb(255, 220, 120),
            ["Reef Titan"] = Rgb(120, 255, 255),
            ["Colossus Reef Titan"] = Rgb(120, 255, 255),
            ["Omnithal"] = Rgb(140, 120, 255),
            ["Awakened Omnithal"] = Rgb(140, 120, 255),
            ["Goldwraith"] = Rgb(255, 210, 70),
            ["Ancient Goldwraith"] = Rgb(255, 210, 70),
            ["Storm Flood"] = Rgb(70, 160, 255),
            ["Tidecrasher Archon"] = Rgb(120, 200, 255),
            ["Megalodon"] = Rgb(255, 60, 60),
            ["Ancient Megalodon"] = Rgb(255, 60, 60),
            ["Phantom Megalodon"] = Rgb(45, 90, 190),
            ["Solar Chorus"] = Rgb(255, 220, 70),
            ["Helios Sunray"] = Rgb(220, 190, 70),
            ["Kerauno Wyrm"] = Rgb(190, 190, 70),
            ["War Surge"] = Rgb(255, 70, 70),
            ["Legionnaire Lamprey"] = Rgb(180, 90, 90),
            ["Kraken"] = Rgb(255, 120, 170),
            ["Ancient Kraken"] = Rgb(255, 120, 170),
            ["Orca Migration"] = Rgb(255, 120, 170),
            ["Whale Migration"] = Rgb(255, 120, 170),
            ["Great White Shark"] = Rgb(255, 120, 170),
            ["Great Hammerhead Shark"] = Rgb(255, 120, 170),
            ["Whale Shark"] = Rgb(255, 120, 170),
            ["Leviathan"] = Rgb(255, 220, 120),
            ["Profane Leviathan"] = Rgb(255, 220, 120),
            ["Skeletal Leviathan"] = Rgb(180, 120, 90),
            ["Beluga"] = Rgb(255, 70, 70),
            ["Narwhal"] = Rgb(255, 70, 70),
            ["Magician Narwhal"] = Rgb(255, 70, 70),
            ["Mosslurker"] = Rgb(255, 70, 70),
            ["Dreadfin"] = Rgb(220, 220, 220),
            ["Scylla"] = Rgb(140, 255, 190),
            ["Mossjaw"] = Rgb(70, 120, 70),
            ["Elder Mossjaw"] = Rgb(70, 120, 70),
            ["Flower Guardian"] = Rgb(180, 255, 120),
            ["Toxic Guardian"] = Rgb(180, 255, 120),
            ["Rotbloom"] = Rgb(180, 120, 90),
            ["Ashclaw"] = Rgb(255, 70, 70),
            ["Bloop Fish"] = Rgb(255, 70, 70),
            ["Frostwyrm"] = Rgb(120, 220, 255),
            ["Wyvern"] = Rgb(120, 180, 120),
            ["Earthquake"] = Rgb(255, 140, 90),
            ["Sunken Chests"] = Rgb(255, 190, 140),
            ["Humpback Whale"] = Rgb(120, 220, 255),
            ["Megamouth Shark"] = Rgb(70, 140, 255),
            ["Baby Bloop Fish"] = Rgb(255, 210, 140),
            ["Colossal Ancient Dragon"] = Rgb(255, 70, 70),
            ["Colossal Blue Dragon"] = Rgb(80, 200, 255),
            ["Colossal Ethereal Dragon"] = Rgb(230, 140, 255),
            ["Olympian Devil"] = Rgb(213, 123, 153),
        };

    private static readonly IReadOnlyDictionary<string, IBrush> Brushes =
        new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sovereign Surge"] = Brush(Colors["Sovereign Surge"]),
            ["Sovereign Storm"] = Brush(Colors["Sovereign Storm"]),
            ["Sovereign Reckoning"] = Brush(Colors["Sovereign Reckoning"]),
            ["Wisp Haunt"] = Brush(Colors["Wisp Haunt"]),
            ["Soul Scourge"] = Brush(Colors["Soul Scourge"]),
            ["Styx Angler"] = Brush(Colors["Styx Angler"]),
            ["Plesiosaur"] = Brush(Colors["Plesiosaur"]),
            ["Pliosaur"] = Brush(Colors["Pliosaur"]),
            ["Ancestral Pliosaur"] = Brush(Colors["Ancestral Pliosaur"]),
            ["Reef Titan"] = Brush(Colors["Reef Titan"]),
            ["Colossus Reef Titan"] = Brush(Colors["Colossus Reef Titan"]),
            ["Omnithal"] = Brush(Colors["Omnithal"]),
            ["Awakened Omnithal"] = Brush(Colors["Awakened Omnithal"]),
            ["Goldwraith"] = Brush(Colors["Goldwraith"]),
            ["Ancient Goldwraith"] = Brush(Colors["Ancient Goldwraith"]),
            ["Storm Flood"] = Brush(Colors["Storm Flood"]),
            ["Tidecrasher Archon"] = Brush(Colors["Tidecrasher Archon"]),
            ["Megalodon"] = Brush(Colors["Megalodon"]),
            ["Ancient Megalodon"] = Brush(Colors["Ancient Megalodon"]),
            ["Phantom Megalodon"] = Brush(Colors["Phantom Megalodon"]),
            ["Solar Chorus"] = Brush(Colors["Solar Chorus"]),
            ["Helios Sunray"] = Brush(Colors["Helios Sunray"]),
            ["Kerauno Wyrm"] = Brush(Colors["Kerauno Wyrm"]),
            ["War Surge"] = Brush(Colors["War Surge"]),
            ["Legionnaire Lamprey"] = Brush(Colors["Legionnaire Lamprey"]),
            ["Kraken"] = Brush(Colors["Kraken"]),
            ["Ancient Kraken"] = Brush(Colors["Ancient Kraken"]),
            ["Orca Migration"] = Brush(Colors["Orca Migration"]),
            ["Whale Migration"] = Brush(Colors["Whale Migration"]),
            ["Great White Shark"] = Brush(Colors["Great White Shark"]),
            ["Great Hammerhead Shark"] = Brush(Colors["Great Hammerhead Shark"]),
            ["Whale Shark"] = Brush(Colors["Whale Shark"]),
            ["Leviathan"] = Brush(Colors["Leviathan"]),
            ["Profane Leviathan"] = Brush(Colors["Profane Leviathan"]),
            ["Skeletal Leviathan"] = Brush(Colors["Skeletal Leviathan"]),
            ["Beluga"] = Brush(Colors["Beluga"]),
            ["Narwhal"] = Brush(Colors["Narwhal"]),
            ["Magician Narwhal"] = Brush(Colors["Magician Narwhal"]),
            ["Mosslurker"] = Brush(Colors["Mosslurker"]),
            ["Dreadfin"] = Brush(Colors["Dreadfin"]),
            ["Scylla"] = Brush(Colors["Scylla"]),
            ["Mossjaw"] = Brush(Colors["Mossjaw"]),
            ["Elder Mossjaw"] = Brush(Colors["Elder Mossjaw"]),
            ["Flower Guardian"] = Brush(Colors["Flower Guardian"]),
            ["Toxic Guardian"] = Brush(Colors["Toxic Guardian"]),
            ["Rotbloom"] = Brush(Colors["Rotbloom"]),
            ["Ashclaw"] = Brush(Colors["Ashclaw"]),
            ["Bloop Fish"] = Brush(Colors["Bloop Fish"]),
            ["Frostwyrm"] = Brush(Colors["Frostwyrm"]),
            ["Wyvern"] = Brush(Colors["Wyvern"]),
            ["Earthquake"] = Brush(Colors["Earthquake"]),
            ["Sunken Chests"] = Brush(Colors["Sunken Chests"]),
            ["Humpback Whale"] = Brush(Colors["Humpback Whale"]),
            ["Megamouth Shark"] = Brush(Colors["Megamouth Shark"]),
            ["Baby Bloop Fish"] = Brush(Colors["Baby Bloop Fish"]),
            ["Colossal Ancient Dragon"] = Brush(Colors["Colossal Ancient Dragon"]),
            ["Colossal Blue Dragon"] = Brush(Colors["Colossal Blue Dragon"]),
            ["Colossal Ethereal Dragon"] = Brush(Colors["Colossal Ethereal Dragon"]),
            ["Olympian Devil"] = Brush(Colors["Olympian Devil"]),
        };

    public static IBrush GetBrush(string? name, bool useColors)
    {
        if (!useColors || string.IsNullOrWhiteSpace(name))
        {
            return DefaultBrush;
        }

        if (string.Equals(name, "Olympian Devil", StringComparison.OrdinalIgnoreCase))
        {
            return OlympianDevilGradientBrush;
        }
        
        if (string.Equals(name, "Colossal Ethereal Dragon", StringComparison.OrdinalIgnoreCase))
        {
            return ColossalEtherealDragonGradientBrush;
        }
        
        return Brushes.TryGetValue(name, out var brush) ? brush : DefaultBrush;
    }

    public static Color GetUiColor(string? huntName)
    {
        if (string.IsNullOrWhiteSpace(huntName))
        {
            return DefaultColor;
        }

        if (string.Equals(huntName, "Olympian Devil", StringComparison.OrdinalIgnoreCase))
        {
            return OlympianDevilEmbedColor;
        }
        
        if (string.Equals(huntName, "Colossal Ethereal Dragon", StringComparison.OrdinalIgnoreCase))
        {
            return ColossalEtherealDragonEmbedColor;
        }
        
        if (string.Equals(huntName, "Colossal Blue Dragon", StringComparison.OrdinalIgnoreCase))
        {
            return ColossalBlueDragonEmbedColor;
        }

        return Colors.TryGetValue(huntName, out var color) ? color : DefaultColor;
    }

    public static string GetDisplayName(string? huntName)
    {
        if (string.IsNullOrWhiteSpace(huntName))
        {
            return string.Empty;
        }

        foreach (var candidate in UiDisplayNames)
        {
            if (string.Equals(candidate, huntName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return huntName.Trim();
    }

    public static int GetDiscordEmbedColor(string? huntName)
    {
        var color = GetUiColor(huntName);
        return (color.R << 16) | (color.G << 8) | color.B;
    }

    private static IBrush Brush(Color color)
    {
        return new SolidColorBrush(color);
    }

    private static Color Rgb(byte r, byte g, byte b)
    {
        return Avalonia.Media.Color.FromArgb(255, r, g, b);
    }

    private static IBrush CreateGradientBrush(Color left, Color middle, Color right)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(left, 0),
                new GradientStop(middle, 0.5),
                new GradientStop(right, 1),
            ],
        };
    }
}
