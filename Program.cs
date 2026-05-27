using Avalonia;
using System;
using System.Runtime.InteropServices;
using Client.Diagnostics;
using Client.Services;

namespace Client;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        EnableDpiAwareness();
        AppLog.Info("Program", "Process started.");
        if (HwidCommand.TryHandle(args))
        {
            return Environment.ExitCode;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void EnableDpiAwareness()
    {
        // Keep input/click coordinates in physical pixels on scaled displays.
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
            {
                SetProcessDpiAwarenessContext((IntPtr)(-4)); // PER_MONITOR_AWARE_V2
                return;
            }
        }
        catch
        {
            // Fallback below.
        }

        try
        {
            SetProcessDPIAware();
        }
        catch
        {
            // Ignore; process may already be DPI-aware.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
