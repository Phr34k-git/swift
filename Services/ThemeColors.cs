using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Client.Services;

public static class ThemeColors
{
    public static IReadOnlyDictionary<string, Color> BuildCustom(CustomThemeAnchors anchors)
    {
        var bg = anchors.Background;
        var surf = anchors.Surface;
        var border = anchors.Border;
        var accent = anchors.Accent;
        var text = anchors.TextPrimary;

        var isDark = RelativeLuminance(bg) < 0.5;

        var textSecondary = Lerp(text, bg, 0.55);
        var accentHover = Lerp(accent, bg, 0.18);
        var accentPressed = Lerp(accent, bg, 0.36);
        var accentForeground = RelativeLuminance(accent) > 0.55 ? bg : Color.FromRgb(0xFF, 0xFF, 0xFF);

        var inputHoverSurface = Lerp(surf, border, 0.35);
        var inputHoverBorder = Lerp(border, text, 0.25);
        var inputFocusSurface = Lerp(surf, bg, 0.5);
        var inputFocusBorder = accentHover;

        var tableSurface = Lerp(surf, bg, 0.4);
        var tableHeaderBackground = Lerp(surf, border, 0.2);

        var sidebarHoverBackground = Lerp(bg, border, 0.35);
        var sidebarSelectedBackground = Lerp(bg, border, 0.6);

        var updateBannerBackground = Lerp(bg, accent, 0.18);

        return new Dictionary<string, Color>
        {
            ["Background"]               = bg,
            ["Surface"]                  = surf,
            ["Border"]                   = border,
            ["Accent"]                   = accent,
            ["AccentHover"]              = accentHover,
            ["AccentPressed"]            = accentPressed,
            ["AccentForeground"]         = accentForeground,
            ["TextPrimary"]              = text,
            ["TextSecondary"]            = textSecondary,
            ["Success"]                  = isDark ? Color.Parse("#4ADE80") : Color.Parse("#15803D"),
            ["Error"]                    = isDark ? Color.Parse("#F87171") : Color.Parse("#DC2626"),
            ["InputHoverSurface"]        = inputHoverSurface,
            ["InputHoverBorder"]         = inputHoverBorder,
            ["InputFocusSurface"]        = inputFocusSurface,
            ["InputFocusBorder"]         = inputFocusBorder,
            ["TableSurface"]             = tableSurface,
            ["TableRowBackground"]       = tableSurface,
            ["TableGap"]                 = bg,
            ["TableHeaderBackground"]    = tableHeaderBackground,
            ["TableEmptyBackground"]     = tableSurface,
            ["TableRowDisabled"]         = tableSurface,
            ["SidebarItemForeground"]             = textSecondary,
            ["SidebarItemHoverForeground"]        = text,
            ["SidebarItemHoverBackground"]        = sidebarHoverBackground,
            ["SidebarItemSelectedForeground"]     = text,
            ["SidebarItemSelectedBackground"]     = sidebarSelectedBackground,
            ["ButtonSecondaryBackground"]         = Color.FromArgb(0, 0, 0, 0),
            ["ButtonSecondaryForeground"]         = text,
            ["ButtonSecondaryBorder"]             = border,
            ["ButtonSecondaryHoverBackground"]    = inputHoverSurface,
            ["ButtonSecondaryHoverBorder"]        = inputHoverBorder,
            ["ButtonSecondaryPressedBackground"]  = inputFocusSurface,
            ["CheckboxBackground"]                = surf,
            ["CheckboxBorder"]                    = textSecondary,
            ["CheckboxHoverBackground"]           = inputHoverSurface,
            ["CheckboxHoverBorder"]               = accent,
            ["CheckboxCheckedBackground"]         = accent,
            ["CheckboxCheckedBorder"]             = accent,
            ["CheckboxCheckForeground"]           = accentForeground,
            ["CheckboxLabelForeground"]           = text,
            ["CheckboxLabelHoverForeground"]      = text,
            ["UpdateBannerBackground"]            = updateBannerBackground,
            ["ThemeCardHoverBackground"]          = inputHoverSurface,
            ["ToggleHoverBackground"]             = inputHoverSurface,
            ["ToggleHoverBorder"]                 = inputHoverBorder,
            ["ToggleCheckedBackground"]           = sidebarSelectedBackground,
            ["ToggleCheckedBorder"]               = accentPressed,
        };
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static double RelativeLuminance(Color c)
        => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;


    public static IReadOnlyDictionary<string, Color> Dark { get; } = new Dictionary<string, Color>
    {
        ["Background"]               = Color.Parse("#0E0E0E"),
        ["Surface"]                  = Color.Parse("#161616"),
        ["Border"]                   = Color.Parse("#2A2A2A"),
        ["Accent"]                   = Color.Parse("#A9CFCB"),
        ["AccentHover"]              = Color.Parse("#8FBFBB"),
        ["AccentPressed"]            = Color.Parse("#789F9B"),
        ["AccentForeground"]         = Color.Parse("#0E0E0E"),
        ["TextPrimary"]              = Color.Parse("#F0F0F0"),
        ["TextSecondary"]            = Color.Parse("#666666"),
        ["Success"]                  = Color.Parse("#4ADE80"),
        ["Error"]                    = Color.Parse("#F87171"),
        ["InputHoverSurface"]        = Color.Parse("#1A1A1A"),
        ["InputHoverBorder"]         = Color.Parse("#3A3A3A"),
        ["InputFocusSurface"]        = Color.Parse("#181818"),
        ["InputFocusBorder"]         = Color.Parse("#5F8581"),
        ["TableSurface"]             = Color.Parse("#141414"),
        ["TableRowBackground"]       = Color.Parse("#141414"),
        ["TableGap"]                 = Color.Parse("#0E0E0E"),
        ["TableHeaderBackground"]    = Color.Parse("#1B1B1B"),
        ["TableEmptyBackground"]     = Color.Parse("#141414"),
        ["TableRowDisabled"]         = Color.Parse("#141414"),
        ["SidebarItemForeground"]             = Color.Parse("#666666"),
        ["SidebarItemHoverForeground"]        = Color.Parse("#F0F0F0"),
        ["SidebarItemHoverBackground"]        = Color.Parse("#1D1F1F"),
        ["SidebarItemSelectedForeground"]     = Color.Parse("#F0F0F0"),
        ["SidebarItemSelectedBackground"]     = Color.Parse("#242424"),
        ["ButtonSecondaryBackground"]         = Color.Parse("#00000000"),
        ["ButtonSecondaryForeground"]         = Color.Parse("#F0F0F0"),
        ["ButtonSecondaryBorder"]             = Color.Parse("#2A2A2A"),
        ["ButtonSecondaryHoverBackground"]    = Color.Parse("#1A1A1A"),
        ["ButtonSecondaryHoverBorder"]        = Color.Parse("#3A3A3A"),
        ["ButtonSecondaryPressedBackground"]  = Color.Parse("#181818"),
        ["CheckboxBackground"]                = Color.Parse("#161616"),
        ["CheckboxBorder"]                    = Color.Parse("#666666"),
        ["CheckboxHoverBackground"]           = Color.Parse("#1A1A1A"),
        ["CheckboxHoverBorder"]               = Color.Parse("#A9CFCB"),
        ["CheckboxCheckedBackground"]         = Color.Parse("#A9CFCB"),
        ["CheckboxCheckedBorder"]             = Color.Parse("#A9CFCB"),
        ["CheckboxCheckForeground"]           = Color.Parse("#0E0E0E"),
        ["CheckboxLabelForeground"]           = Color.Parse("#F0F0F0"),
        ["CheckboxLabelHoverForeground"]      = Color.Parse("#F0F0F0"),
        ["UpdateBannerBackground"]            = Color.Parse("#0F1D1C"),
        ["ThemeCardHoverBackground"]          = Color.Parse("#1A1A1A"),
        ["ToggleHoverBackground"]             = Color.Parse("#141A20"),
        ["ToggleHoverBorder"]                 = Color.Parse("#273849"),
        ["ToggleCheckedBackground"]           = Color.Parse("#121C26"),
        ["ToggleCheckedBorder"]               = Color.Parse("#3A566F"),
    };

    public static IReadOnlyDictionary<string, Color> Light { get; } = new Dictionary<string, Color>
    {
        ["Background"]               = Color.Parse("#F7F8F8"),
        ["Surface"]                  = Color.Parse("#FFFFFF"),
        ["Border"]                   = Color.Parse("#D8DEDC"),
        ["Accent"]                   = Color.Parse("#5F8581"),
        ["AccentHover"]              = Color.Parse("#4F736F"),
        ["AccentPressed"]            = Color.Parse("#415F5B"),
        ["AccentForeground"]         = Color.Parse("#FFFFFF"),
        ["TextPrimary"]              = Color.Parse("#171717"),
        ["TextSecondary"]            = Color.Parse("#66706D"),
        ["Success"]                  = Color.Parse("#15803D"),
        ["Error"]                    = Color.Parse("#DC2626"),
        ["InputHoverSurface"]        = Color.Parse("#F1F4F3"),
        ["InputHoverBorder"]         = Color.Parse("#C8D1CE"),
        ["InputFocusSurface"]        = Color.Parse("#FFFFFF"),
        ["InputFocusBorder"]         = Color.Parse("#5F8581"),
        ["TableSurface"]             = Color.Parse("#FFFFFF"),
        ["TableRowBackground"]       = Color.Parse("#FFFFFF"),
        ["TableGap"]                 = Color.Parse("#EEF1F0"),
        ["TableHeaderBackground"]    = Color.Parse("#F1F4F3"),
        ["TableEmptyBackground"]     = Color.Parse("#FFFFFF"),
        ["TableRowDisabled"]         = Color.Parse("#F6F7F7"),
        ["SidebarItemForeground"]             = Color.Parse("#66706D"),
        ["SidebarItemHoverForeground"]        = Color.Parse("#171717"),
        ["SidebarItemHoverBackground"]        = Color.Parse("#E6EDEC"),
        ["SidebarItemSelectedForeground"]     = Color.Parse("#171717"),
        ["SidebarItemSelectedBackground"]     = Color.Parse("#DDE8E7"),
        ["ButtonSecondaryBackground"]         = Color.Parse("#00000000"),
        ["ButtonSecondaryForeground"]         = Color.Parse("#171717"),
        ["ButtonSecondaryBorder"]             = Color.Parse("#D8DEDC"),
        ["ButtonSecondaryHoverBackground"]    = Color.Parse("#F1F4F3"),
        ["ButtonSecondaryHoverBorder"]        = Color.Parse("#C8D1CE"),
        ["ButtonSecondaryPressedBackground"]  = Color.Parse("#E6EDEC"),
        ["CheckboxBackground"]                = Color.Parse("#FFFFFF"),
        ["CheckboxBorder"]                    = Color.Parse("#8D9996"),
        ["CheckboxHoverBackground"]           = Color.Parse("#F1F4F3"),
        ["CheckboxHoverBorder"]               = Color.Parse("#5F8581"),
        ["CheckboxCheckedBackground"]         = Color.Parse("#5F8581"),
        ["CheckboxCheckedBorder"]             = Color.Parse("#5F8581"),
        ["CheckboxCheckForeground"]           = Color.Parse("#FFFFFF"),
        ["CheckboxLabelForeground"]           = Color.Parse("#171717"),
        ["CheckboxLabelHoverForeground"]      = Color.Parse("#171717"),
        ["UpdateBannerBackground"]            = Color.Parse("#EAF3F2"),
        ["ThemeCardHoverBackground"]          = Color.Parse("#F1F4F3"),
        ["ToggleHoverBackground"]             = Color.Parse("#EEF4F6"),
        ["ToggleHoverBorder"]                 = Color.Parse("#C8D1CE"),
        ["ToggleCheckedBackground"]           = Color.Parse("#DDE8E7"),
        ["ToggleCheckedBorder"]               = Color.Parse("#5F8581"),
    };

    // Soft-pink dark palette. Keeps the same readable dark base as Dark/Slate;
    // accents and selection/hover tints are pink instead of teal.
    public static IReadOnlyDictionary<string, Color> Pink { get; } = new Dictionary<string, Color>
    {
        ["Background"]               = Color.Parse("#161013"),
        ["Surface"]                  = Color.Parse("#1F1719"),
        ["Border"]                   = Color.Parse("#3A2A30"),
        ["Accent"]                   = Color.Parse("#F4A8C4"),
        ["AccentHover"]              = Color.Parse("#E891B2"),
        ["AccentPressed"]            = Color.Parse("#C57495"),
        ["AccentForeground"]         = Color.Parse("#1A0F14"),
        ["TextPrimary"]              = Color.Parse("#F5EEF0"),
        ["TextSecondary"]            = Color.Parse("#9C7E87"),
        ["Success"]                  = Color.Parse("#4ADE80"),
        ["Error"]                    = Color.Parse("#F87171"),
        ["InputHoverSurface"]        = Color.Parse("#241A1D"),
        ["InputHoverBorder"]         = Color.Parse("#503843"),
        ["InputFocusSurface"]        = Color.Parse("#211A1D"),
        ["InputFocusBorder"]         = Color.Parse("#E891B2"),
        ["TableSurface"]             = Color.Parse("#1B1417"),
        ["TableRowBackground"]       = Color.Parse("#1B1417"),
        ["TableGap"]                 = Color.Parse("#161013"),
        ["TableHeaderBackground"]    = Color.Parse("#241A1E"),
        ["TableEmptyBackground"]     = Color.Parse("#1B1417"),
        ["TableRowDisabled"]         = Color.Parse("#1B1417"),
        ["SidebarItemForeground"]             = Color.Parse("#9C7E87"),
        ["SidebarItemHoverForeground"]        = Color.Parse("#F5EEF0"),
        ["SidebarItemHoverBackground"]        = Color.Parse("#251A1F"),
        ["SidebarItemSelectedForeground"]     = Color.Parse("#F5EEF0"),
        ["SidebarItemSelectedBackground"]     = Color.Parse("#2E2026"),
        ["ButtonSecondaryBackground"]         = Color.Parse("#00000000"),
        ["ButtonSecondaryForeground"]         = Color.Parse("#F5EEF0"),
        ["ButtonSecondaryBorder"]             = Color.Parse("#3A2A30"),
        ["ButtonSecondaryHoverBackground"]    = Color.Parse("#241A1D"),
        ["ButtonSecondaryHoverBorder"]        = Color.Parse("#503843"),
        ["ButtonSecondaryPressedBackground"]  = Color.Parse("#211A1D"),
        ["CheckboxBackground"]                = Color.Parse("#1F1719"),
        ["CheckboxBorder"]                    = Color.Parse("#9C7E87"),
        ["CheckboxHoverBackground"]           = Color.Parse("#241A1D"),
        ["CheckboxHoverBorder"]               = Color.Parse("#F4A8C4"),
        ["CheckboxCheckedBackground"]         = Color.Parse("#F4A8C4"),
        ["CheckboxCheckedBorder"]             = Color.Parse("#F4A8C4"),
        ["CheckboxCheckForeground"]           = Color.Parse("#1A0F14"),
        ["CheckboxLabelForeground"]           = Color.Parse("#F5EEF0"),
        ["CheckboxLabelHoverForeground"]      = Color.Parse("#F5EEF0"),
        ["UpdateBannerBackground"]            = Color.Parse("#291720"),
        ["ThemeCardHoverBackground"]          = Color.Parse("#241A1D"),
        ["ToggleHoverBackground"]             = Color.Parse("#241A1D"),
        ["ToggleHoverBorder"]                 = Color.Parse("#503843"),
        ["ToggleCheckedBackground"]           = Color.Parse("#2A1920"),
        ["ToggleCheckedBorder"]               = Color.Parse("#E891B2"),
    };

    public static IReadOnlyDictionary<string, Color> Slate { get; } = new Dictionary<string, Color>
    {
        ["Background"]               = Color.Parse("#111416"),
        ["Surface"]                  = Color.Parse("#181D20"),
        ["Border"]                   = Color.Parse("#30383C"),
        ["Accent"]                   = Color.Parse("#9AB7C8"),
        ["AccentHover"]              = Color.Parse("#86A8BD"),
        ["AccentPressed"]            = Color.Parse("#7191A3"),
        ["AccentForeground"]         = Color.Parse("#0B1114"),
        ["TextPrimary"]              = Color.Parse("#EEF2F3"),
        ["TextSecondary"]            = Color.Parse("#8D999F"),
        ["Success"]                  = Color.Parse("#46C981"),
        ["Error"]                    = Color.Parse("#EF767A"),
        ["InputHoverSurface"]        = Color.Parse("#20272B"),
        ["InputHoverBorder"]         = Color.Parse("#465158"),
        ["InputFocusSurface"]        = Color.Parse("#1B2226"),
        ["InputFocusBorder"]         = Color.Parse("#86A8BD"),
        ["TableSurface"]             = Color.Parse("#151A1D"),
        ["TableRowBackground"]       = Color.Parse("#151A1D"),
        ["TableGap"]                 = Color.Parse("#0F1214"),
        ["TableHeaderBackground"]    = Color.Parse("#1E2529"),
        ["TableEmptyBackground"]     = Color.Parse("#151A1D"),
        ["TableRowDisabled"]         = Color.Parse("#171B1E"),
        ["SidebarItemForeground"]             = Color.Parse("#8D999F"),
        ["SidebarItemHoverForeground"]        = Color.Parse("#EEF2F3"),
        ["SidebarItemHoverBackground"]        = Color.Parse("#20282C"),
        ["SidebarItemSelectedForeground"]     = Color.Parse("#F4F8F9"),
        ["SidebarItemSelectedBackground"]     = Color.Parse("#263137"),
        ["ButtonSecondaryBackground"]         = Color.Parse("#00000000"),
        ["ButtonSecondaryForeground"]         = Color.Parse("#EEF2F3"),
        ["ButtonSecondaryBorder"]             = Color.Parse("#30383C"),
        ["ButtonSecondaryHoverBackground"]    = Color.Parse("#20272B"),
        ["ButtonSecondaryHoverBorder"]        = Color.Parse("#465158"),
        ["ButtonSecondaryPressedBackground"]  = Color.Parse("#252E33"),
        ["CheckboxBackground"]                = Color.Parse("#14191C"),
        ["CheckboxBorder"]                    = Color.Parse("#566166"),
        ["CheckboxHoverBackground"]           = Color.Parse("#20272B"),
        ["CheckboxHoverBorder"]               = Color.Parse("#7B8B92"),
        ["CheckboxCheckedBackground"]         = Color.Parse("#9AB7C8"),
        ["CheckboxCheckedBorder"]             = Color.Parse("#9AB7C8"),
        ["CheckboxCheckForeground"]           = Color.Parse("#0B1114"),
        ["CheckboxLabelForeground"]           = Color.Parse("#EEF2F3"),
        ["CheckboxLabelHoverForeground"]      = Color.Parse("#EEF2F3"),
        ["UpdateBannerBackground"]            = Color.Parse("#172226"),
        ["ThemeCardHoverBackground"]          = Color.Parse("#20272B"),
        ["ToggleHoverBackground"]             = Color.Parse("#20272B"),
        ["ToggleHoverBorder"]                 = Color.Parse("#465158"),
        ["ToggleCheckedBackground"]           = Color.Parse("#1F2B31"),
        ["ToggleCheckedBorder"]               = Color.Parse("#7191A3"),
    };
}
