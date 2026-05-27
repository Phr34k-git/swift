using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Client.Views;

/// <summary>
/// Placeholder settings page view.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Creates the settings view.
    /// </summary>
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
