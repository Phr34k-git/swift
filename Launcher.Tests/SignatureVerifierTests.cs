using System.Text;
using Launcher;
using Xunit;

namespace Launcher.Tests;

public class SignatureVerifierTests
{
    // Exact bytes of tools/release/releases/1.0.0/manifest.json (CRLF, base64-encoded)
    // so the test is byte-for-byte identical to what was signed.
    private const string RealManifestBase64 =
        "ew0KICAidmVyc2lvbiI6ICIxLjAuMCIsDQogICJyZWxlYXNlZF9hdCI6ICIyMDI2LTA1LTE2VDEzOjAyOjMxWiIsDQogICJmaWxlcyI6IFsNCiAgICB7DQogICAgICAibmFtZSI6ICJhdl9saWJnbGVzdjIuZGxsIiwNCiAgICAgICJzaGEyNTYiOiAiOWIyMDNlNDAzMjNiNDlkYWQyOTU0NmE1MmI4YjY3ZDIwMGJiYThmZjRjYWI5NzA5YTc5Y2VkZTIzYmE4NDdkNCIsDQogICAgICAic2l6ZSI6IDU0MjYxNzYNCiAgICB9LA0KICAgIHsNCiAgICAgICJuYW1lIjogImxpYkhhcmZCdXp6U2hhcnAuZGxsIiwNCiAgICAgICJzaGEyNTYiOiAiMTQ1YWRlOTYzZWJhNDI3MDI3ZDVlMTM4MWRiNWQ1MWEwZWY5ZTk4ZTlmZWY5ZWZiZmE2MDc0Y2EyMGRhMjQ2ZiIsDQogICAgICAic2l6ZSI6IDE4MTYwODgNCiAgICB9LA0KICAgIHsNCiAgICAgICJuYW1lIjogImxpYkhhcmZCdXp6U2hhcnAucGRiIiwNCiAgICAgICJzaGEyNTYiOiAiNDMwMGQ3NWQ1YWYwOWZiYjc5ZDU2NWQ5YTE4NTczMjQzYTkzMjE0YzgxMWQ0ZjI0YTMzZjExZjBkNDI0MmQ5MyIsDQogICAgICAic2l6ZSI6IDIwOTE4MjcyDQogICAgfSwNCiAgICB7DQogICAgICAibmFtZSI6ICJsaWJTa2lhU2hhcnAuZGxsIiwNCiAgICAgICJzaGEyNTYiOiAiNmQ2ZWNhNjRlYzMzM2RhZWQ3ODg1OGE2MzdiNTRjNTQ1YWEwMTU1ODIwNGI4ZTJhZTg4NzA2OTJhY2FkYTEyNyIsDQogICAgICAic2l6ZSI6IDExNjI4NTc2DQogICAgfSwNCiAgICB7DQogICAgICAibmFtZSI6ICJsaWJTa2lhU2hhcnAucGRiIiwNCiAgICAgICJzaGEyNTYiOiAiMjk0MGZhZTVkOGVhOTYwMTJiMzg1MGZmOWU4YTJjYzNiOTZiMjhiOWU0NjEzZTk0MjJiNTc0OTJkMzJmNTI3OSIsDQogICAgICAic2l6ZSI6IDg0MDMzNTM2DQogICAgfSwNCiAgICB7DQogICAgICAibmFtZSI6ICJvZmZzZXRzLmhwcCIsDQogICAgICAic2hhMjU2IjogIjY1YjcxZTcxZjYyNjU4NDFlNjEyNGE3YTE3YWUyNTAxNmU5ZGUzMjcyZDExODU0NTExY2Y5MDBhY2YwMDZmZDIiLA0KICAgICAgInNpemUiOiAyNTY4OQ0KICAgIH0sDQogICAgew0KICAgICAgIm5hbWUiOiAiU3dpZnQuZXhlIiwNCiAgICAgICJzaGEyNTYiOiAiMTZhODAwMmEzMjNkNjVjMDcwNjYyZjdiZjk1OThkMWI0M2M3MDAzOTM3NjcwZGQ1MTQ2ZWU4YjIwY2ExZTg4OCIsDQogICAgICAic2l6ZSI6IDM4MTE2MzUyDQogICAgfSwNCiAgICB7DQogICAgICAibmFtZSI6ICJTd2lmdC5wZGIiLA0KICAgICAgInNoYTI1NiI6ICI4MTk2ZDc4N2FiZWI2MTYwZDhkYzAyN2U1YzI1ZTVjN2E1YTM4NWZiOGE4ODMxNWIwYTlhYjVhOWIyODI0ZDYyIiwNCiAgICAgICJzaXplIjogMjIwNzk0ODgwDQogICAgfQ0KICBdLA0KICAiaGlzdG9yeSI6IFtdDQp9";

    // Real manifest.sig from tools/release/releases/1.0.0/manifest.sig
    private const string RealSig =
        "tDytxN3r5S4g1mD2GJQb3V29lel5O3J8cixUftrLafhKju6JT0dKvOcWpx9mqZkut60X/ZvMjzWO8cKlOrioCA==";

    private static byte[] RealManifestBytes => Convert.FromBase64String(RealManifestBase64);

    [Fact]
    public void AcceptsRealSignature()
    {
        bool result = SignatureVerifier.Verify(RealManifestBytes, RealSig);
        Assert.True(result);
    }

    [Fact]
    public void RejectsTamperedManifest()
    {
        var bytes = RealManifestBytes;
        bytes[0] ^= 0xFF; // flip bits in first byte
        bool result = SignatureVerifier.Verify(bytes, RealSig);
        Assert.False(result);
    }

    [Fact]
    public void RejectsInvalidBase64Sig()
    {
        bool result = SignatureVerifier.Verify(RealManifestBytes, "not-valid-base64!!!");
        Assert.False(result);
    }

    [Fact]
    public void RejectsTooShortSig()
    {
        bool result = SignatureVerifier.Verify(RealManifestBytes, "");
        Assert.False(result);
    }
}
