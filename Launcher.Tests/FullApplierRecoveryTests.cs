using System.Security.Cryptography;
using System.Text.Json;
using Launcher;
using Xunit;

namespace Launcher.Tests;

public class FullApplierRecoveryTests
{
    [Fact]
    public void SweepRecoveryRestoresBakWhenHashMatchesCurrentManifest()
    {
        using var temp = new TempInstall();
        var backupPath = Path.Combine(temp.Dir, "Swift.exe.bak");
        File.WriteAllText(backupPath, "known-good");
        temp.WriteManifest("Swift.exe", Sha256Hex(backupPath));

        FullApplier.SweepRecovery(temp.Dir);

        Assert.True(File.Exists(Path.Combine(temp.Dir, "Swift.exe")));
        Assert.Equal("known-good", File.ReadAllText(Path.Combine(temp.Dir, "Swift.exe")));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public void SweepRecoveryRefusesBakWhenHashDoesNotMatchCurrentManifest()
    {
        using var temp = new TempInstall();
        var backupPath = Path.Combine(temp.Dir, "Swift.exe.bak");
        File.WriteAllText(backupPath, "tampered");
        temp.WriteManifest("Swift.exe", new string('a', 64));

        FullApplier.SweepRecovery(temp.Dir);

        Assert.False(File.Exists(Path.Combine(temp.Dir, "Swift.exe")));
        Assert.True(File.Exists(backupPath));
    }

    private static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class TempInstall : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TempInstall()
        {
            Directory.CreateDirectory(Dir);
        }

        public void WriteManifest(string fileName, string sha256)
        {
            var manifest = new Manifest(
                "1.0.0",
                "2026-05-18T00:00:00Z",
                new[] { new ManifestFile(fileName, sha256, 0L) },
                Array.Empty<PatchEntry>(),
                null);
            var json = JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.Manifest);
            File.WriteAllText(Path.Combine(Dir, "current.manifest.json"), json);
        }

        public void Dispose()
        {
            if (Directory.Exists(Dir))
            {
                Directory.Delete(Dir, recursive: true);
            }
        }
    }
}
