using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// Represents one flat sidebar navigation item in the logged-in shell.
/// </summary>
public sealed class ShellNavigationItemViewModel : ViewModelBase, IDisposable
{
    private readonly object _page;
    private readonly RelayCommand _navigateCommand;
    private bool _isSelected;

    /// <summary>
    /// Creates a sidebar navigation item.
    /// </summary>
    public ShellNavigationItemViewModel(string name, object page, Action<ShellNavigationItemViewModel> navigate)
    {
        Name = name;
        _page = page;
        _navigateCommand = new RelayCommand(_ =>
        {
            navigate(this);
            return Task.CompletedTask;
        });

        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>
    /// Gets the display name shown in the sidebar.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the page view model associated with this item.
    /// </summary>
    public object Page => _page;

    /// <summary>
    /// Gets the command that navigates to this item.
    /// </summary>
    public ICommand NavigateCommand => _navigateCommand;

    /// <summary>
    /// Gets whether this item represents the active page.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(Foreground));
        }
    }

    /// <summary>
    /// Gets the item background brush for the current state.
    /// </summary>
    public IBrush Background => IsSelected
        ? GetAppBrush("SidebarItemSelectedBackground", SolidColorBrush.Parse("#242424"))
        : Brushes.Transparent;

    /// <summary>
    /// Gets the item foreground brush for the current state.
    /// </summary>
    public IBrush Foreground => IsSelected
        ? GetAppBrush("SidebarItemSelectedForeground", SolidColorBrush.Parse("#F0F0F0"))
        : GetAppBrush("SidebarItemForeground", SolidColorBrush.Parse("#666666"));

    public void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(AppTheme _)
    {
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Foreground));
    }

    private static IBrush GetAppBrush(string key, IBrush fallback)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var res) == true && res is IBrush brush)
            return brush;

        return fallback;
    }
}
