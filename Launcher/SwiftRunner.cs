using System.Diagnostics;

namespace Launcher;

internal static class SwiftRunner
{
    internal static async Task<int> RunAsync(
        string swiftExePath,
        string installDir,
        string? updatePreviewVersion,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = swiftExePath,
            WorkingDirectory = installDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (updatePreviewVersion is not null)
            psi.Environment["SWIFT_UPDATE_PREVIEW_VERSION"] = updatePreviewVersion;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Swift.exe");

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
