using System.Text;
using System.Text.Json;
using Launcher;
using Xunit;

namespace Launcher.Tests;

public class ManifestSchemaTests
{
    private const string ManifestJson = """
        {
          "version": "1.0.0",
          "released_at": "2026-05-16T13:02:31Z",
          "files": [
            {
              "name": "av_libglesv2.dll",
              "sha256": "9b203e40323b49dad29546a52b8b67d200bba8ff4cab9709a79cede23ba847d4",
              "size": 5426176
            },
            {
              "name": "libHarfBuzzSharp.dll",
              "sha256": "145ade963eba427027d5e1381db5d51a0ef9e98e9fef9efbfa6074ca20da246f",
              "size": 1816088
            },
            {
              "name": "libHarfBuzzSharp.pdb",
              "sha256": "4300d75d5af09fbb79d565d9a18573243a93214c811d4f24a33f11f0d4242d93",
              "size": 20918272
            },
            {
              "name": "libSkiaSharp.dll",
              "sha256": "6d6eca64ec333daed78858a637b54c545aa01558204b8e2ae8870692acada127",
              "size": 11628576
            },
            {
              "name": "libSkiaSharp.pdb",
              "sha256": "2940fae5d8ea96012b3850ff9e8a2cc3b96b28b9e4613e9422b57492d32f5279",
              "size": 84033536
            },
            {
              "name": "offsets.hpp",
              "sha256": "65b71e71f6265841e6124a7a17ae25016e9de3272d11854511cf900acf006fd2",
              "size": 25689
            },
            {
              "name": "Swift.exe",
              "sha256": "16a8002a323d65c070662f7bf9598d1b43c7003937670dd5146ee8b20ca1e888",
              "size": 38116352
            },
            {
              "name": "Swift.pdb",
              "sha256": "8196d787abeb6160d8dc027e5c25e5c7a5a385fb8a88315b0a9ab5a9b2824d62",
              "size": 220794880
            }
          ],
          "history": []
        }
        """;

    [Fact]
    public void Deserializes_Version()
    {
        var manifest = JsonSerializer.Deserialize(ManifestJson, LauncherJsonContext.Default.Manifest)!;
        Assert.Equal("1.0.0", manifest.Version);
    }

    [Fact]
    public void Deserializes_FileCount()
    {
        var manifest = JsonSerializer.Deserialize(ManifestJson, LauncherJsonContext.Default.Manifest)!;
        Assert.Equal(8, manifest.Files.Count);
    }

    [Fact]
    public void Deserializes_HistoryEmpty()
    {
        var manifest = JsonSerializer.Deserialize(ManifestJson, LauncherJsonContext.Default.Manifest)!;
        Assert.Empty(manifest.History);
    }

    [Fact]
    public void Deserializes_SwiftExeEntry()
    {
        var manifest = JsonSerializer.Deserialize(ManifestJson, LauncherJsonContext.Default.Manifest)!;
        var swift = manifest.Files.First(f => f.Name == "Swift.exe");
        Assert.Equal("Swift.exe", swift.Name);
        Assert.Equal("16a8002a323d65c070662f7bf9598d1b43c7003937670dd5146ee8b20ca1e888", swift.Sha256);
    }
}
