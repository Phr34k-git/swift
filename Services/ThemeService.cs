using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Client;

namespace Client.Services;

// Note: StarlightTruffleWormGradient is a LinearGradientBrush and is intentionally absent from
// the palettes — mutating its stop colors requires a separate mechanism and is not handled here.
public static class ThemeService
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static CustomThemeAnchors CustomAnchors { get; private set; } = CustomThemeAnchors.DefaultDark();

    /// <summary>
    /// True once a custom theme has been explicitly established — either loaded from disk
    /// or edited/seeded by the user. Used to prevent re-seeding from a preset on every
    /// switch back to <see cref="AppTheme.Custom"/>.
    /// </summary>
    public static bool HasCustomAnchors { get; private set; }

    public static event Action<AppTheme>? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;
        Current = theme;
        var palette = ResolvePalette(theme);
        app.RequestedThemeVariant = IsLightPalette(palette) ? ThemeVariant.Light : ThemeVariant.Dark;
        ApplyPalette(app, palette);
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>
    /// Replaces the stored custom anchors. If <see cref="Current"/> is already
    /// <see cref="AppTheme.Custom"/>, the live palette is refreshed.
    /// </summary>
    public static void SetCustomAnchors(CustomThemeAnchors anchors, bool apply = true)
    {
        CustomAnchors = anchors.Clone();
        HasCustomAnchors = true;
        if (apply && Current == AppTheme.Custom)
        {
            Apply(AppTheme.Custom);
        }
    }

    private static System.Collections.Generic.IReadOnlyDictionary<string, Color> ResolvePalette(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeColors.Light,
        AppTheme.Slate => ThemeColors.Slate,
        AppTheme.Pink => ThemeColors.Pink,
        AppTheme.Custom => ThemeColors.BuildCustom(CustomAnchors),
        _ => ThemeColors.Dark,
    };

    private static bool IsLightPalette(System.Collections.Generic.IReadOnlyDictionary<string, Color> palette)
    {
        if (palette.TryGetValue("Background", out var bg))
        {
            return (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) / 255.0 >= 0.5;
        }
        return false;
    }

    private static void ApplyPalette(Application app, System.Collections.Generic.IReadOnlyDictionary<string, Color> palette)
    {
        foreach (var (key, color) in palette)
        {
            if (app.Resources.TryGetResource(key, null, out var resource) && resource is SolidColorBrush brush)
                brush.Color = color;
        }
    }
}
