using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Client.ViewModels;

namespace Client.Views;

/// <summary>
/// Account page view.
/// </summary>
public partial class AccountView : UserControl
{
    /// <summary>
    /// Creates the account view.
    /// </summary>
    public AccountView()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm)
        {
            vm.Attach();
            _ = vm.LoadAsync();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm)
        {
            vm.Detach();
        }
    }
}
