using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Client.Views;

/// <summary>
/// Discord login view.
/// </summary>
public partial class LoginView : UserControl
{
    /// <summary>
    /// Creates the Discord login view.
    /// </summary>
    public LoginView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
