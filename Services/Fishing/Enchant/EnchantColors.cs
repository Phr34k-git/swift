using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using DrawingColor = System.Drawing.Color;

namespace Client.Services.Fishing;

internal static class EnchantColors
{
    private sealed record GradientColors(Color Start, Color End);

    private static readonly IReadOnlyDictionary<string, DrawingColor> Colors =
        new Dictionary<string, DrawingColor>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Abyssal"] = DrawingColor.FromArgb(52, 70, 180),
            ["Blessed"] = DrawingColor.FromArgb(255, 70, 170),
            ["Blood Reckoning"] = DrawingColor.FromArgb(180, 40, 40),
            ["Breezed"] = DrawingColor.FromArgb(170, 190, 255),
            ["Chaotic"] = DrawingColor.FromArgb(230, 230, 230),
            ["Chronos"] = DrawingColor.FromArgb(0, 90, 255),
            ["Clever"] = DrawingColor.FromArgb(255, 120, 170),
            ["Controlled"] = DrawingColor.FromArgb(190, 150, 255),
            ["Divine"] = DrawingColor.FromArgb(200, 255, 255),
            ["Flashline"] = DrawingColor.FromArgb(255, 255, 255),
            ["Ghastly"] = DrawingColor.FromArgb(120, 255, 170),
            ["Hasty"] = DrawingColor.FromArgb(255, 210, 90),
            ["Hunter"] = DrawingColor.FromArgb(100, 0, 255),
            ["Insight"] = DrawingColor.FromArgb(120, 255, 120),
            ["Long"] = DrawingColor.FromArgb(255, 180, 60),
            ["Lucky"] = DrawingColor.FromArgb(120, 255, 180),
            ["Momentum"] = DrawingColor.FromArgb(230, 190, 120),
            ["Mutated"] = DrawingColor.FromArgb(140, 255, 120),
            ["Noir"] = DrawingColor.FromArgb(255, 255, 255),
            ["Quality"] = DrawingColor.FromArgb(180, 255, 80),
            ["Resilient"] = DrawingColor.FromArgb(120, 255, 210),
            ["Scavenger"] = DrawingColor.FromArgb(255, 180, 70),
            ["Sea King"] = DrawingColor.FromArgb(70, 110, 255),
            ["Scrapper"] = DrawingColor.FromArgb(255, 130, 40),
            ["Steady"] = DrawingColor.FromArgb(220, 200, 180),
            ["Storming"] = DrawingColor.FromArgb(255, 255, 90),
            ["Swift"] = DrawingColor.FromArgb(180, 255, 255),
            ["Unbreakable"] = DrawingColor.FromArgb(230, 180, 255),
            ["Wormhole"] = DrawingColor.FromArgb(140, 80, 255),
            ["Anomalous"] = DrawingColor.FromArgb(255, 255, 40, 40),
            ["Ferocious"] = DrawingColor.FromArgb(255, 180, 0, 0),
            ["Herculean"] = DrawingColor.FromArgb(255, 255, 230, 40),
            ["Immortal"] = DrawingColor.FromArgb(255, 230, 220, 255),
            ["Mystical"] = DrawingColor.FromArgb(255, 180, 210, 255),
            ["Piercing"] = DrawingColor.FromArgb(255, 40, 220, 200),
            ["Quantum"] = DrawingColor.FromArgb(255, 255, 0, 255),
            ["Sea Overlord"] = DrawingColor.FromArgb(255, 80, 180, 255),
            ["Cryogenic"] = DrawingColor.FromArgb(255, 120, 220, 255),
            ["Glittered"] = DrawingColor.FromArgb(255, 255, 235, 140),
            ["Invincible"] = DrawingColor.FromArgb(255, 255, 90, 0),
            ["Overclocked"] = DrawingColor.FromArgb(255, 0, 255, 230),
            ["Sea Prince"] = DrawingColor.FromArgb(255, 90, 120, 255),
            ["Tenacity"] = DrawingColor.FromArgb(255, 255, 255, 180),
            ["Tryhard"] = DrawingColor.FromArgb(255, 255, 0, 0),
            ["Vicious"] = DrawingColor.FromArgb(255, 255, 120, 90),
            ["Wise"] = DrawingColor.FromArgb(255, 180, 120, 255),
            ["Rage"] = DrawingColor.FromArgb(255, 255, 40, 40),
            ["Greed"] = DrawingColor.FromArgb(255, 255, 210, 0),
            ["Fractured"] = DrawingColor.FromArgb(255, 220, 210, 190),
            ["Putrid"] = DrawingColor.FromArgb(255, 70, 110, 60),
            ["Pharaohs Curse"] = DrawingColor.FromArgb(255, 170, 140, 90),
            ["Weak"] = DrawingColor.FromArgb(255, 180, 180, 180),
            ["Wobbly"] = DrawingColor.FromArgb(255, 120, 120, 120),
            ["Blessed Song"] = DrawingColor.FromArgb(255, 0, 180, 255),
            ["Valentine's"] = DrawingColor.FromArgb(255, 210, 120, 170),
            ["Cupid"] = DrawingColor.FromArgb(255, 240, 230, 210),
            ["Santa"] = DrawingColor.FromArgb(255, 255, 70, 70),
            ["Gingerbread"] = DrawingColor.FromArgb(255, 170, 90, 50),
            ["Merry"] = DrawingColor.FromArgb(255, 0, 180, 0),
            ["Peppermint"] = DrawingColor.FromArgb(255, 255, 0, 0),
            ["Frightful"] = DrawingColor.FromArgb(255, 140, 90, 180),
            ["Spooky"] = DrawingColor.FromArgb(255, 220, 140, 50),
            ["Eerie"] = DrawingColor.FromArgb(255, 100, 255, 140),
        };

    private static readonly IReadOnlyDictionary<string, GradientColors> Gradients =
        new Dictionary<string, GradientColors>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Propensity Spirit"] = new GradientColors(Color.Parse("#F89DC6"), Color.Parse("#C496EF")),
            ["Starforged Spirit"] = new GradientColors(Color.Parse("#C0ACD8"), Color.Parse("#EDB5BA")),
            ["Menacing Spirit"] = new GradientColors(Color.Parse("#F3AA57"), Color.Parse("#FE766D")),
            ["Swift Might"] = new GradientColors(Color.Parse("#ABB9F9"), Color.Parse("#C797F7")),
            ["Magnitude Might"] = new GradientColors(Color.Parse("#FBC0A8"), Color.Parse("#BF94EF")),
            ["Immortal Might"] = new GradientColors(Color.Parse("#E8C2B8"), Color.Parse("#FFB5B5")),
            ["Stonewake Crown"] = new GradientColors(Color.Parse("#A6A8A9"), Color.Parse("#D3D5C4")),
            ["Glimmering Crown"] = new GradientColors(Color.Parse("#FDCBE1"), Color.Parse("#FBF2A6")),
        };

    public static DrawingColor GetEnchantColor(string? enchantName)
    {
        if (!string.IsNullOrWhiteSpace(enchantName) &&
            Colors.TryGetValue(enchantName.Trim(), out var color))
        {
            return color;
        }

        return DrawingColor.White;
    }

    public static IEnumerable<string> GetKnownEnchantNames()
    {
        foreach (var enchant in Colors.Keys)
        {
            yield return enchant;
        }

        foreach (var enchant in Gradients.Keys)
        {
            yield return enchant;
        }
    }

    public static IBrush GetEnchantBrush(string? enchantName)
    {
        if (!string.IsNullOrWhiteSpace(enchantName) &&
            Gradients.TryGetValue(enchantName.Trim(), out var gradient))
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(gradient.Start, 0),
                    new GradientStop(gradient.End, 1),
                },
            };
        }

        var color = GetEnchantColor(enchantName);
        return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
    }

    public static string FindEnchantName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var enchant in GetKnownEnchantNames())
        {
            if (text.Contains(enchant, System.StringComparison.OrdinalIgnoreCase))
            {
                return enchant;
            }
        }

        return string.Empty;
    }
}
