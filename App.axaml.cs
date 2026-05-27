using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Client.Services;

namespace Client;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var userSettings = new UserSettingsStore().Load();
            var theme = !string.IsNullOrWhiteSpace(userSettings.Theme) &&
                        Enum.TryParse<AppTheme>(userSettings.Theme, true, out var parsedTheme)
                ? parsedTheme
                : AppSettingsService.Load().Theme;
            ThemeService.Apply(theme);
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
