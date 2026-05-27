using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Launcher;

internal static class SignatureVerifier
{
    // Raw 32-byte Ed25519 public key (extracted from keys/swift_public.key SubjectPublicKeyInfo DER)
    private static readonly byte[] PublicKey =
        Convert.FromBase64String("VIhWAALruj4Il0mAX/VjuVfhbrTcshsLmP7kxCyBtcs=");

    internal static bool Verify(byte[] manifestBytes, string signatureBase64)
    {
        try
        {
            var sigBytes = Convert.FromBase64String(signatureBase64.Trim());
            var key = new Ed25519PublicKeyParameters(PublicKey, 0);
            var signer = new Ed25519Signer();
            signer.Init(false, key);
            signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
            return signer.VerifySignature(sigBytes);
        }
        catch (Exception ex)
        {
            Log.Warn($"Signature verification threw: {ex.Message}");
            return false;
        }
    }
}
