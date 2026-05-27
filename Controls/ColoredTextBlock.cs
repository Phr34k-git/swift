using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Client.ViewModels;

namespace Client.Controls;

public sealed class ColoredTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<ColoredTextLineViewModel>?> LinesProperty =
        AvaloniaProperty.Register<ColoredTextBlock, IReadOnlyList<ColoredTextLineViewModel>?>(nameof(Lines));

    public static readonly StyledProperty<bool> UseGradientsProperty =
        AvaloniaProperty.Register<ColoredTextBlock, bool>(nameof(UseGradients), true);

    public IReadOnlyList<ColoredTextLineViewModel>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public bool UseGradients
    {
        get => GetValue(UseGradientsProperty);
        set => SetValue(UseGradientsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LinesProperty || change.Property == UseGradientsProperty)
        {
            RebuildInlines();
        }
    }

    private void RebuildInlines()
    {
        Inlines?.Clear();
        if (Lines is null || Lines.Count == 0)
        {
            Text = "---";
            return;
        }

        Text = null;
        Inlines ??= [];
        for (var lineIndex = 0; lineIndex < Lines.Count; lineIndex++)
        {
            if (lineIndex > 0)
            {
                Inlines.Add(new LineBreak());
            }

            foreach (var segment in Lines[lineIndex].Segments)
            {
                Inlines.Add(new Run(segment.Text)
                {
                    Foreground = GetForegroundBrush(segment.Brush),
                });
            }
        }
    }

    private IBrush GetForegroundBrush(IBrush brush)
    {
        if (!UseGradients || brush is not ISolidColorBrush solidBrush)
        {
            return brush;
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ShiftLightness(solidBrush.Color, 34), 0),
                new GradientStop(solidBrush.Color, 0.52),
                new GradientStop(ShiftLightness(solidBrush.Color, -24), 1),
            },
        };
    }

    private static Color ShiftLightness(Color color, int amount)
    {
        return Color.FromArgb(
            color.A,
            ShiftChannel(color.R, amount),
            ShiftChannel(color.G, amount),
            ShiftChannel(color.B, amount));
    }

    private static byte ShiftChannel(byte value, int amount)
    {
        return (byte)Math.Clamp(value + amount, 0, 255);
    }
}
