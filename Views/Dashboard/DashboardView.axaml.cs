using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Client.Views;

/// <summary>
/// Placeholder dashboard view.
/// </summary>
public partial class DashboardView : UserControl
{
    /// <summary>
    /// Creates the dashboard view.
    /// </summary>
    public DashboardView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
