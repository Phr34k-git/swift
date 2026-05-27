using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Client.Controls;

public sealed class AppKeyValueList : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<AppKeyValueItem>?> ItemsProperty =
        AvaloniaProperty.Register<AppKeyValueList, IReadOnlyList<AppKeyValueItem>?>(nameof(Items));

    public IReadOnlyList<AppKeyValueItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public AppKeyValueList()
    {
        BuildRows();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            BuildRows();
        }
    }

    private void BuildRows()
    {
        var items = Items ?? [];

        var rows = new Grid
        {
            RowDefinitions = new RowDefinitions(),
            Background = GetBrush("TableGap", "#0E0E0E"),
            RowSpacing = 1,
            ClipToBounds = true,
        };

        for (var index = 0; index < items.Count; index++)
        {
            rows.RowDefinitions.Add(new RowDefinition(new GridLength(42)));
            AddRow(rows, items[index], index);
        }

        Content = new Border
        {
            Background = GetBrush("TableGap", "#0E0E0E"),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = rows,
        };
    }

    private void AddRow(Grid rows, AppKeyValueItem item, int rowIndex)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = GetBrush("Surface", "#161616"),
            MinHeight = 42,
        };

        row.Children.Add(new TextBlock
        {
            Text = item.Entry,
            FontFamily = GetFontFamily("UIFont"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = GetBrush("TextPrimary", "#F0F0F0"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 12, 0),
        });

        Control value;
        if (item.ColoredLines is not null)
        {
            value = new ColoredTextBlock
            {
                Lines = item.ColoredLines,
                FontFamily = GetFontFamily("UIFont"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 20, 0),
                MaxWidth = 260,
            };
        }
        else
        {
            value = new TextBlock
            {
                Text = item.Value,
                FontFamily = GetFontFamily("UIFont"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = GetBrush(item.ValueForegroundResourceKey, "#F0F0F0"),
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 20, 0),
                MaxWidth = 260,
            };
        }
        Grid.SetColumn(value, 1);
        row.Children.Add(value);

        Grid.SetRow(row, rowIndex);
        rows.Children.Add(row);
    }

    private IBrush GetBrush(string key, string fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallback));
    }

    private FontFamily GetFontFamily(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is FontFamily fontFamily)
        {
            return fontFamily;
        }

        return FontFamily.Default;
    }
}
