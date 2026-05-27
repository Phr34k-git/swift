using System.IO.Compression;
using System.Security.Cryptography;

namespace Launcher;

/// <summary>
/// Full-payload update path. Used when the patch chain has no entry for
/// (localVer -> targetVer) — most importantly downgrades after a rollback,
/// or first-time fills when the local install drifted from the manifest.
///
/// Trust model: full.zip's SHA-256 is signed inside the manifest (already
/// verified by the caller). After extracting, each individual file is also
/// hash-checked against manifest.files[] before being swapped in.
/// </summary>
internal static class FullApplier
{
    /// <summary>
    /// Extract <paramref name="fullZipPath"/> into a staging dir and verify
    /// every extracted file's SHA-256 against the manifest. Returns the
    /// staging directory path on success. Throws on any tamper / mismatch.
    /// </summary>
    internal static string ExtractAndVerify(
        string installDir,
        Manifest manifest,
        string fullZipPath)
    {
        var stagingRoot = Path.Combine(installDir, "staging");
        var extractDir  = Path.Combine(stagingRoot, $"full-{manifest.Version}");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        Directory.CreateDirectory(extractDir);

        ZipFile.ExtractToDirectory(fullZipPath, extractDir);

        // Build a quick lookup of expected hashes from the manifest.
        var expected = manifest.Files.ToDictionary(
            f => f.Name,
            f => f.Sha256,
            StringComparer.OrdinalIgnoreCase);

        // Verify every extracted file is one we expected and its hash matches.
        foreach (var path in Directory.EnumerateFiles(extractDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!expected.TryGetValue(name, out var wantHash))
                throw new InvalidOperationException(
                    $"full.zip contains unexpected file '{name}' (not in manifest).");
            var gotHash = Sha256Hex(path);
            if (!string.Equals(gotHash, wantHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"hash mismatch on extracted '{name}' (got {gotHash}, expected {wantHash}).");
        }

        // Verify the manifest's file list is fully covered — refusing to apply
        // a zip that's missing files (could brick the install).
        foreach (var f in manifest.Files)
        {
            var path = Path.Combine(extractDir, f.Name);
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    $"full.zip is missing manifest file '{f.Name}'.");
        }

        return extractDir;
    }

    /// <summary>
    /// Atomically swap every file from <paramref name="extractDir"/> into
    /// <paramref name="installDir"/>. The Launcher.exe is handled specially:
    /// on Windows a running .exe can be renamed but not overwritten, so we
    /// rename the current one to .old and move the new one into its place.
    /// </summary>
    internal static void ApplyExtracted(
        string installDir,
        Manifest manifest,
        string extractDir)
    {
        var currentLauncherPath = Path.Combine(installDir, "Launcher.exe");
        var runningLauncherPath = Environment.ProcessPath ?? currentLauncherPath;
        var isReplacingSelf =
            string.Equals(Path.GetFullPath(currentLauncherPath),
                          Path.GetFullPath(runningLauncherPath),
                          StringComparison.OrdinalIgnoreCase);

        foreach (var f in manifest.Files)
        {
            var srcPath = Path.Combine(extractDir, f.Name);
            var dstPath = Path.Combine(installDir, f.Name);

            // Backup or rename-out the existing file. Launcher.exe must use
            // a .old suffix because it's running; everything else uses .bak.
            var isLauncherExe = isReplacingSelf &&
                string.Equals(f.Name, "Launcher.exe", StringComparison.OrdinalIgnoreCase);
            var holdSuffix = isLauncherExe ? ".old" : ".bak";
            var holdPath = dstPath + holdSuffix;

            if (File.Exists(dstPath))
            {
                if (File.Exists(holdPath)) File.Delete(holdPath);
                File.Move(dstPath, holdPath);
            }

            File.Move(srcPath, dstPath);

            // Best-effort: drop the .bak right away once the new file is in
            // place, except for Launcher.exe.old (we hold that until the
            // running process exits — SweepRecovery on next start cleans it).
            if (!isLauncherExe && File.Exists(holdPath))
            {
                try { File.Delete(holdPath); } catch { /* sweep handles it */ }
            }
        }

        try { Directory.Delete(extractDir, recursive: true); } catch { /* sweep */ }
    }

    /// <summary>
    /// Recover from a partial apply: if a payload file is missing but a .bak
    /// of it is present, restore the .bak. Also sweep up any leftover .old
    /// files (always safe to delete after process restart). Called once at
    /// the top of every Launcher run.
    /// </summary>
    internal static void SweepRecovery(string installDir)
    {
        if (!Directory.Exists(installDir)) return;

        var manifest = LocalState.ReadCurrentManifest(installDir);
        var expected = manifest?.Files.ToDictionary(
            f => f.Name,
            f => f.Sha256,
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(installDir, "*.bak", SearchOption.TopDirectoryOnly))
        {
            var liveName = path[..^".bak".Length];
            if (!File.Exists(liveName))
            {
                var liveFileName = Path.GetFileName(liveName);
                if (expected is not null &&
                    expected.TryGetValue(liveFileName, out var expectedHash) &&
                    !string.Equals(Sha256Hex(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn($"Refusing to restore {liveFileName} from .bak: hash does not match current manifest.");
                    continue;
                }

                Log.Warn($"Detected incomplete apply: restoring {Path.GetFileName(liveName)} from .bak.");
                try { File.Move(path, liveName); }
                catch (Exception ex)
                {
                    Log.Warn($"  could not restore {Path.GetFileName(liveName)}: {ex.Message}");
                }
            }
            else
            {
                // Live file is present; the .bak is residue from a successful
                // apply that didn't get cleaned up. Safe to delete.
                try { File.Delete(path); } catch { /* leave it */ }
            }
        }

        foreach (var path in Directory.EnumerateFiles(installDir, "*.old", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(path); } catch { /* file in use; try next start */ }
        }
    }

    private static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
