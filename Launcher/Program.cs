using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("Launcher.Tests")]

namespace Launcher;

internal static partial class Program
{
    private static readonly string SwiftExeName = "Swift.exe";
    private const int RestartExitCode = 86;
    private static readonly Regex SafeFileName =
        new(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex SafePatchPath =
        new(@"^patches/[A-Za-z0-9._\-]+\.patch$", RegexOptions.Compiled);

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var installDir = AppContext.BaseDirectory;
        var swiftExe   = Path.Combine(installDir, SwiftExeName);

        // Recover from a previously interrupted apply — restore any payload
        // file from its .bak if the live copy is missing, and clean up the
        // *.old residue left after a self-replace of Launcher.exe.
        FullApplier.SweepRecovery(installDir);

        // Main update-launch loop. Bound the number of full-apply iterations
        // so a manifest pinned to "version N != local" can't induce infinite
        // download cycles.
        const int maxFullApplyPerRun = 2;
        var fullAppliesThisRun = 0;

        // Self-heal if Swift.exe was removed out from under us (commonly an
        // antivirus quarantine — Defender frequently flags game-automation
        // binaries on established installs). Without this, the shortcut
        // appears broken to the user and "reinstall to jumpstart" is their
        // only recovery. TryFullApplyAsync re-downloads full.zip and writes
        // a fresh install atomically; it does not require an existing Swift.exe.
        if (!File.Exists(swiftExe))
        {
            Log.Warn("Swift.exe missing on startup — attempting full-apply recovery (likely AV quarantine).");
            var recovered = false;
            try
            {
                recovered = await TryFullApplyAsync(installDir, swiftExe, localVer: "missing");
            }
            catch (Exception ex)
            {
                Log.Warn($"Recovery full-apply threw: {ex.Message}");
            }

            if (recovered) fullAppliesThisRun++;

            if (!File.Exists(swiftExe))
            {
                Log.Error("Swift.exe still missing after recovery attempt. Cannot start.");
                return 1;
            }
            Log.Info("Recovery succeeded; Swift.exe restored.");
        }
        while (true)
        {
            // L1: Read local version
            var localVer = LocalState.GetLocalVersion(swiftExe);
            Log.Info($"Local version: {localVer}");

            // L2+L3+L4+L5: Try to stage an update. If the patch chain has no
            // entry for (localVer -> targetVer) we fall back to full.zip and
            // apply it inline before launching Swift — that's the only path
            // that handles downgrades and arbitrary-distance forward jumps.
            string? stagedVersion = null;
            bool fullApplyHappened = false;
            try
            {
                stagedVersion = await TryStageUpdateAsync(installDir, swiftExe, localVer);
                if (stagedVersion is null && fullAppliesThisRun < maxFullApplyPerRun)
                {
                    fullApplyHappened = await TryFullApplyAsync(installDir, swiftExe, localVer);
                    if (fullApplyHappened) fullAppliesThisRun++;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Update check failed: {ex.Message}");
            }

            // If a full apply happened, loop back so we read the new version
            // and launch the right binary on this same run.
            if (fullApplyHappened) continue;

            // L6: Launch Swift.exe
            Log.Info($"Launching Swift.exe (staged update: {stagedVersion ?? "none"})");
            int exitCode = await SwiftRunner.RunAsync(swiftExe, installDir, stagedVersion);
            Log.Info($"Swift.exe exited with code {exitCode}");

            // L7: Handle exit
            if (exitCode == RestartExitCode)
            {
                var pending = LocalState.ReadPendingUpdate(installDir);
                if (pending is null)
                {
                    Log.Warn("Exit code 86 but no pending-update.json found. Exiting.");
                    return exitCode;
                }

                bool applied = await TryApplyUpdateAsync(installDir, swiftExe, pending);
                if (!applied)
                {
                    // Apply failed — loop back without update env var so Swift starts clean
                    Log.Warn("Update apply failed. Relaunching without update.");
                    continue;
                }

                Log.Info("Update applied. Restarting.");
                continue; // loop back to L1 to verify and relaunch
            }

            return exitCode;
        }
    }

    private static async Task<string?> TryStageUpdateAsync(
        string installDir, string swiftExe, string localVer)
    {
        // L2: Fetch
        byte[] manifestBytes;
        string sigText;
        try
        {
            manifestBytes = await ReleaseClient.GetManifestBytesAsync();
            sigText       = await ReleaseClient.GetSignatureAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Network error during update check: {ex.Message}");
            return null;
        }

        // L3: Verify signature
        if (!SignatureVerifier.Verify(manifestBytes, sigText))
        {
            Log.Warn("Manifest signature verification failed. Skipping update.");
            return null;
        }

        // Parse manifest
        Manifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(manifestBytes, LauncherJsonContext.Default.Manifest)
                ?? throw new InvalidOperationException("Manifest deserialized to null");
        }
        catch (Exception ex)
        {
            Log.Warn($"Manifest parse failed: {ex.Message}");
            return null;
        }

        // Validate manifest file names for path traversal
        if (!ValidateManifest(manifest)) return null;

        var targetVer = manifest.Version;

        // L4: Check if update needed
        if (localVer == targetVer)
        {
            Log.Info("Already up to date.");
            return null;
        }

        // Find patch entry
        var hist = manifest.History.FirstOrDefault(h => h.From == localVer && h.To == targetVer);
        if (hist is null)
        {
            Log.Warn($"No patch path from {localVer} to {targetVer}. Skipping update.");
            return null;
        }

        // Confirm only Swift.exe differs (v1 restriction)
        foreach (var f in manifest.Files)
        {
            if (string.Equals(f.Name, "Swift.exe",    StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(f.Name, "Launcher.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            var localPath = Path.Combine(installDir, f.Name);
            if (!File.Exists(localPath))
            {
                Log.Warn($"Local file missing: {f.Name}. Skipping update (out of scope for v1).");
                return null;
            }
            var localHash = Sha256Hex(localPath);
            if (!string.Equals(localHash, f.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"Non-exe file {f.Name} hash mismatch (local={localHash}, manifest={f.Sha256}). Out of scope for v1.");
                return null;
            }
        }

        // L5: Stage update
        var stagingDir = Path.Combine(installDir, "staging");
        Directory.CreateDirectory(stagingDir);

        var patchFileName = Path.GetFileName(hist.File);
        var patchPath     = Path.Combine(stagingDir, patchFileName);
        var newExePath    = Path.Combine(stagingDir, "Swift.exe.new");

        // Download patch
        try { await ReleaseClient.DownloadPatchAsync(hist.File, patchPath); }
        catch (Exception ex)
        {
            Log.Warn($"Patch download failed: {ex.Message}");
            TryCleanStaging(stagingDir);
            return null;
        }

        // Verify patch hash
        var patchHash = Sha256Hex(patchPath);
        if (!string.Equals(patchHash, hist.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"Patch hash mismatch (got={patchHash}, expected={hist.Sha256})");
            TryCleanStaging(stagingDir);
            return null;
        }

        // Apply patch
        try { Patcher.Apply(swiftExe, patchPath, newExePath); }
        catch (Exception ex)
        {
            Log.Warn($"Patch apply failed: {ex.Message}");
            TryCleanStaging(stagingDir);
            return null;
        }

        // Verify new exe hash
        var swiftEntry = manifest.Files.FirstOrDefault(f =>
            string.Equals(f.Name, "Swift.exe", StringComparison.OrdinalIgnoreCase));
        if (swiftEntry is null)
        {
            Log.Warn("Manifest has no Swift.exe entry.");
            TryCleanStaging(stagingDir);
            return null;
        }
        var newHash = Sha256Hex(newExePath);
        if (!string.Equals(newHash, swiftEntry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"Patched Swift.exe hash mismatch (got={newHash}, expected={swiftEntry.Sha256})");
            TryCleanStaging(stagingDir);
            return null;
        }

        // Write pending-update.json
        var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes);
        var pending = new PendingUpdate(targetVer, newExePath, manifestJson);
        LocalState.WritePendingUpdate(installDir, pending);

        Log.Info($"Update staged: {localVer} -> {targetVer}");
        return targetVer;
    }

    /// <summary>
    /// Fall-back update path: download the version's full.zip, verify each
    /// payload file's hash against the manifest, and atomically swap them
    /// into the install directory. Used when no patch path exists between
    /// localVer and the manifest's targetVer (covers downgrades after a
    /// server rollback and arbitrary-distance forward jumps).
    ///
    /// Returns true if a swap happened (caller must loop back to re-read the
    /// local version); false if no apply was needed or the apply failed.
    /// </summary>
    private static async Task<bool> TryFullApplyAsync(
        string installDir, string swiftExe, string localVer)
    {
        byte[] manifestBytes;
        string  sigText;
        try
        {
            manifestBytes = await ReleaseClient.GetManifestBytesAsync();
            sigText       = await ReleaseClient.GetSignatureAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Full-apply manifest fetch failed: {ex.Message}");
            return false;
        }

        if (!SignatureVerifier.Verify(manifestBytes, sigText))
        {
            Log.Warn("Manifest signature verification failed (full-apply path).");
            return false;
        }

        Manifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(manifestBytes, LauncherJsonContext.Default.Manifest)
                ?? throw new InvalidOperationException("Manifest deserialized to null");
        }
        catch (Exception ex)
        {
            Log.Warn($"Manifest parse failed (full-apply path): {ex.Message}");
            return false;
        }

        if (!ValidateManifest(manifest)) return false;

        if (manifest.Version == localVer)
            return false;   // already on the right version

        if (manifest.FullZip is null)
        {
            Log.Warn($"Manifest has no full_zip block. Cannot fall back from {localVer} -> {manifest.Version}.");
            return false;
        }

        var stagingDir = Path.Combine(installDir, "staging");
        Directory.CreateDirectory(stagingDir);
        var zipPath = Path.Combine(stagingDir, $"full-{manifest.Version}.zip");

        Log.Info($"Full apply: downloading {manifest.Version} ({manifest.FullZip.Size:N0} bytes) — local was {localVer}.");
        try
        {
            await ReleaseClient.DownloadFullZipAsync(manifest.Version, zipPath);
        }
        catch (Exception ex)
        {
            Log.Warn($"full.zip download failed: {ex.Message}");
            TryCleanStaging(stagingDir);
            return false;
        }

        var zipHash = Sha256Hex(zipPath);
        if (!string.Equals(zipHash, manifest.FullZip.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"full.zip hash mismatch (got={zipHash}, expected={manifest.FullZip.Sha256}).");
            TryCleanStaging(stagingDir);
            return false;
        }

        string extractDir;
        try
        {
            extractDir = FullApplier.ExtractAndVerify(installDir, manifest, zipPath);
        }
        catch (Exception ex)
        {
            Log.Warn($"full.zip extract/verify failed: {ex.Message}");
            TryCleanStaging(stagingDir);
            return false;
        }

        try
        {
            FullApplier.ApplyExtracted(installDir, manifest, extractDir);
        }
        catch (Exception ex)
        {
            Log.Error($"full.zip apply failed mid-swap: {ex.Message}. Recovery sweep will restore on next start.");
            TryCleanStaging(stagingDir);
            return false;
        }

        var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes);
        LocalState.WriteCurrentManifest(installDir, manifestJson);
        LocalState.DeletePendingUpdate(installDir);
        try { File.Delete(zipPath); } catch { /* sweep */ }

        Log.Info($"Full apply complete: now on {manifest.Version}.");
        return true;
    }

    private static async Task<bool> TryApplyUpdateAsync(
        string installDir, string swiftExe, PendingUpdate pending)
    {
        var bakPath    = swiftExe + ".bak";
        var stagingDir = Path.Combine(installDir, "staging");

        try
        {
            // Backup current exe
            File.Move(swiftExe, bakPath, overwrite: true);

            // Move new exe in
            File.Move(pending.NewExePath, swiftExe, overwrite: true);

            // Write current manifest
            LocalState.WriteCurrentManifest(installDir, pending.ManifestJson);

            // Cleanup
            File.Delete(bakPath);
            TryCleanStaging(stagingDir);
            LocalState.DeletePendingUpdate(installDir);

            Log.Info($"Applied update to version {pending.Version}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Apply failed: {ex.Message}");
            // Attempt rollback
            try
            {
                if (!File.Exists(swiftExe) && File.Exists(bakPath))
                    File.Move(bakPath, swiftExe);
            }
            catch (Exception rex)
            {
                Log.Error($"Rollback also failed: {rex.Message}. Install may be corrupted.");
            }
            LocalState.DeletePendingUpdate(installDir);
            TryCleanStaging(stagingDir);
            return false;
        }
    }

    internal static bool ValidateManifest(Manifest manifest)
    {
        foreach (var f in manifest.Files)
        {
            if (!SafeFileName.IsMatch(f.Name) || f.Name.Contains("..") ||
                f.Name.Contains('/') || f.Name.Contains('\\'))
            {
                Log.Warn($"Manifest contains unsafe file name: {f.Name}");
                return false;
            }
        }
        foreach (var h in manifest.History)
        {
            if (!SafePatchPath.IsMatch(h.File))
            {
                Log.Warn($"Manifest contains unsafe patch path: {h.File}");
                return false;
            }
        }
        return true;
    }

    private static string Sha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryCleanStaging(string stagingDir)
    {
        try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); }
        catch { /* best-effort */ }
    }
}
