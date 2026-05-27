using Xunit;

namespace Launcher.Tests;

public sealed class ReleaseClientSourceTests
{
    [Fact]
    public void DownloadFileUsesExclusiveFileShare()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Launcher",
            "ReleaseClient.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("File.OpenWrite", source);
        Assert.Contains("FileShare.None", source);
    }
}
