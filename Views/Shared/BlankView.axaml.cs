using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Client.Views;

/// <summary>
/// Empty startup view.
/// </summary>
public partial class BlankView : UserControl
{
    /// <summary>
    /// Creates the empty startup view.
    /// </summary>
    public BlankView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
