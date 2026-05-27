using System;
using System.Diagnostics;
using System.Linq;

namespace Client.Services;

internal static class BrowserLauncher
{
    public static bool TryOpen(string url, out string error)
    {
        return TryOpen(url, StartProcess, out error);
    }

    internal static bool TryOpen(
        string url,
        Func<ProcessStartInfo, Process?> start,
        out string error)
    {
        var failures = new System.Collections.Generic.List<string>();

        foreach (var startInfo in BuildLaunchAttempts(url))
        {
            try
            {
                start(startInfo);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failures.Add($"{startInfo.FileName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        error = string.Join(" | ", failures);
        return false;
    }

    internal static ProcessStartInfo[] BuildLaunchAttempts(string url)
    {
        var shell = new ProcessStartInfo(url)
        {
            UseShellExecute = true,
        };

        var rundll = new ProcessStartInfo("rundll32.exe")
        {
            UseShellExecute = false,
        };
        rundll.ArgumentList.Add("url.dll,FileProtocolHandler");
        rundll.ArgumentList.Add(url);

        // Edge is pre-installed on all Windows 10/11 machines; use it as a
        // reliable fallback when no default browser handler is registered.
        var edge = new ProcessStartInfo("msedge.exe")
        {
            UseShellExecute = false,
        };
        edge.ArgumentList.Add(url);

        var explorer = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = false,
        };
        explorer.ArgumentList.Add(url);

        return [shell, rundll, edge, explorer];
    }

    private static Process? StartProcess(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo);
    }
}
