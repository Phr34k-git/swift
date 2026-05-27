using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia;

namespace Client.Views;

public partial class HuntDetectView : UserControl
{
    public HuntDetectView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is StyledElement source)
        {
            for (var current = source; current is not null; current = current.Parent as StyledElement)
            {
                if (current is TextBox)
                {
                    return;
                }
            }
        }

        Focus(NavigationMethod.Pointer);
    }
}
