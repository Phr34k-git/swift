using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Services;

internal enum RobloxVersionCheckResult
{
    NotChecked,
    RobloxNotFound,
    UwpNotSupported,
    Match,
    Mismatch,
    LatestUnknown,
}

internal sealed record RobloxVersionCheck(
    RobloxVersionCheckResult Result,
    string? RunningVersion,
    string? LatestVersion);

internal static class RobloxVersionService
{
    // Roblox installs each client at .../Versions/version-<hex>/RobloxPlayerBeta.exe;
    // the same hash appears in clientVersionUpload from clientsettingscdn.
    private static readonly Regex VersionHashPattern = new(
        @"version-[a-f0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<RobloxVersionCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsUwpRobloxRunning())
        {
            return new RobloxVersionCheck(RobloxVersionCheckResult.UwpNotSupported, null, null);
        }

        var running = TryGetRunningVersion();
        if (running is null)
        {
            return new RobloxVersionCheck(RobloxVersionCheckResult.RobloxNotFound, null, null);
        }

        var latest = await TryGetLatestVersionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(latest))
        {
            return new RobloxVersionCheck(RobloxVersionCheckResult.LatestUnknown, running, null);
        }

        return string.Equals(running, latest, StringComparison.OrdinalIgnoreCase)
            ? new RobloxVersionCheck(RobloxVersionCheckResult.Match, running, latest)
            : new RobloxVersionCheck(RobloxVersionCheckResult.Mismatch, running, latest);
    }

    private static bool IsUwpRobloxRunning()
    {
        try
        {
            return Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                .Any(p =>
                {
                    try
                    {
                        var path = p.MainModule?.FileName;
                        return path is not null &&
                               path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                });
        }
        catch { return false; }
    }

    private static string? TryGetRunningVersion()
    {
        try
        {
            var process = Process.GetProcessesByName("RobloxPlayerBeta")
                .OrderByDescending(SafeStartTimeTicks)
                .FirstOrDefault();
            process ??= Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(SafeStartTimeTicks)
                .FirstOrDefault();

            if (process is null)
            {
                return null;
            }

            // MainModule.FileName requires the same access rights as the host has;
            // throws for processes the current user can't open. That's fine — we
            // just treat it as "can't determine" and skip the warning.
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var match = VersionHashPattern.Match(path);
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryGetLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await ApiHttp.SharedClient
                .GetAsync("/api/v1/swift/latest-rbx-version", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var hash = ApiHttp.TryReadString(document.RootElement, "version_hash");
            // Empty hash from the API is the "warnings suppressed" signal set
            // by /ops/rbx-version — surface it the same as a fetch failure so
            // the client takes no action.
            return string.IsNullOrWhiteSpace(hash) ? null : hash;
        }
        catch
        {
            return null;
        }
    }

    private static long SafeStartTimeTicks(Process process)
    {
        try { return process.StartTime.Ticks; }
        catch { return 0; }
    }
}
