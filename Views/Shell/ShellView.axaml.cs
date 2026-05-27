using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Client.Views;

/// <summary>
/// Logged-in application shell with sidebar navigation.
/// </summary>
public partial class ShellView : UserControl
{
    /// <summary>
    /// Creates the shell view.
    /// </summary>
    public ShellView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
