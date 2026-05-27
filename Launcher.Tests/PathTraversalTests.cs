using Launcher;
using Xunit;

namespace Launcher.Tests;

public class PathTraversalTests
{
    private static Manifest MakeManifest(string fileName, string? patchFile = null)
    {
        var files = new List<ManifestFile>
        {
            new ManifestFile(fileName, "aabbccdd", 1234)
        };

        var history = patchFile is null
            ? Array.Empty<PatchEntry>()
            : new[] { new PatchEntry("1.0.0", "1.0.1", patchFile, "aabbccdd", 9999) };

        return new Manifest("1.0.0", "2026-01-01T00:00:00Z", files, history, null);
    }

    [Fact]
    public void AcceptsSafeManifest()
    {
        var manifest = MakeManifest("Swift.exe", "patches/1.0.0-1.0.1.patch");
        Assert.True(Program.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsDotDotInFileName()
    {
        var manifest = MakeManifest("..\\foo");
        Assert.False(Program.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsSlashInFileName()
    {
        var manifest = MakeManifest("some/path");
        Assert.False(Program.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsBackslashInFileName()
    {
        var manifest = MakeManifest("some\\path");
        Assert.False(Program.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsAbsolutePathInFileName()
    {
        var manifest = MakeManifest("C:\\evil");
        Assert.False(Program.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsUnsafePatchPath()
    {
        // We need a valid file name but an unsafe patch path
        // Build manifest manually: safe file, unsafe history entry
        var files = new List<ManifestFile> { new ManifestFile("Swift.exe", "aabbccdd", 1234) };
        var history = new[] { new PatchEntry("1.0.0", "1.0.1", "../../etc/passwd", "aabbccdd", 9999) };
        var manifest = new Manifest("1.0.0", "2026-01-01T00:00:00Z", files, history, null);
        Assert.False(Program.ValidateManifest(manifest));
    }
}
