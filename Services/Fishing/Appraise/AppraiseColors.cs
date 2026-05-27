using System.Collections.Generic;
using Avalonia.Media;
using DrawingColor = System.Drawing.Color;

namespace Client.Services.Fishing;

internal static class AppraiseColors
{
    private static readonly IReadOnlyDictionary<string, DrawingColor> Colors =
        new Dictionary<string, DrawingColor>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Shiny"] = DrawingColor.FromArgb(255, 245, 180),
            ["Sparkling"] = DrawingColor.FromArgb(255, 240, 170),
            ["Big"] = DrawingColor.FromArgb(120, 255, 120),
            ["Giant"] = DrawingColor.FromArgb(120, 255, 120),
            ["Small"] = DrawingColor.FromArgb(140, 255, 220),
            ["Tiny"] = DrawingColor.FromArgb(140, 255, 220),
            ["Albino"] = DrawingColor.FromArgb(255, 235, 235, 235),
            ["Darkened"] = DrawingColor.FromArgb(255, 110, 110, 110),
            ["Negative"] = DrawingColor.FromArgb(255, 120, 80, 255),
            ["Glossy"] = DrawingColor.FromArgb(255, 120, 240, 255),
            ["Lunar"] = DrawingColor.FromArgb(255, 190, 170, 255),
            ["Translucent"] = DrawingColor.FromArgb(255, 120, 255, 200),
            ["Electric"] = DrawingColor.FromArgb(255, 255, 255, 0),
            ["Hexed"] = DrawingColor.FromArgb(255, 255, 0, 0),
            ["Silver"] = DrawingColor.FromArgb(255, 210, 210, 210),
            ["Frozen"] = DrawingColor.FromArgb(255, 100, 255, 255),
            ["Mosaic"] = DrawingColor.FromArgb(255, 255, 170, 255),
            ["Scorched"] = DrawingColor.FromArgb(255, 255, 90, 40),
            ["Amber"] = DrawingColor.FromArgb(255, 180, 40, 0),
            ["Abyssal"] = DrawingColor.FromArgb(255, 0, 0, 255),
            ["Coral"] = DrawingColor.FromArgb(255, 255, 120, 180),
            ["Decayed"] = DrawingColor.FromArgb(255, 220, 220, 220),
            ["Poisoned"] = DrawingColor.FromArgb(255, 170, 120, 255),
            ["Fossilized"] = DrawingColor.FromArgb(255, 170, 130, 110),
            ["Vined"] = DrawingColor.FromArgb(255, 120, 220, 120),
            ["Crimson"] = DrawingColor.FromArgb(255, 255, 60, 60),
            ["Honey"] = DrawingColor.FromArgb(255, 255, 190, 40),
            ["Midas"] = DrawingColor.FromArgb(255, 255, 170, 40),
            ["Boreal"] = DrawingColor.FromArgb(255, 180, 170, 140),
            ["Fallen"] = DrawingColor.FromArgb(255, 120, 60, 40),
            ["Greedy"] = DrawingColor.FromArgb(255, 255, 210, 0),
            ["Spirit"] = DrawingColor.FromArgb(255, 150, 120, 255),
            ["Mourned"] = DrawingColor.FromArgb(255, 0, 0, 0),
            ["Mythical"] = DrawingColor.FromArgb(255, 255, 80, 170),
            ["Shrouded"] = DrawingColor.FromArgb(255, 180, 255, 180),
        };

    public static DrawingColor GetAppraiseColor(string? mutationName)
    {
        if (!string.IsNullOrWhiteSpace(mutationName) &&
            Colors.TryGetValue(mutationName.Trim(), out var color))
        {
            return color;
        }

        return DrawingColor.White;
    }

    public static IEnumerable<string> GetKnownMutationNames()
    {
        return Colors.Keys;
    }

    public static IBrush GetAppraiseBrush(string? mutationName)
    {
        var color = GetAppraiseColor(mutationName);
        return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
    }
}
