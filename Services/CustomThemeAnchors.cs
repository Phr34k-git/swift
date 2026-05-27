using Avalonia.Media;

namespace Client.Services;

public sealed class CustomThemeAnchors
{
    public Color Background { get; set; }
    public Color Surface { get; set; }
    public Color Border { get; set; }
    public Color Accent { get; set; }
    public Color TextPrimary { get; set; }

    public CustomThemeAnchors Clone() => new()
    {
        Background = Background,
        Surface = Surface,
        Border = Border,
        Accent = Accent,
        TextPrimary = TextPrimary,
    };

    public static CustomThemeAnchors DefaultDark() => new()
    {
        Background = Color.Parse("#0E0E0E"),
        Surface = Color.Parse("#161616"),
        Border = Color.Parse("#2A2A2A"),
        Accent = Color.Parse("#A9CFCB"),
        TextPrimary = Color.Parse("#F0F0F0"),
    };
}
