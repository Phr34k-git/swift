using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Client.Views;

public partial class SettingsClientView : UserControl
{
    public SettingsClientView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

public sealed class BoolToBorderBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value is true;
        var key = selected ? "Accent" : "Border";
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true
            && resource is IBrush brush)
            return brush;
        return selected ? Brushes.SteelBlue : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
