using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace StonesHotspot.Licensing.Codes;

/// <summary>Ed25519 signatures (RFC 8032) through BouncyCastle: 32-byte keys, 64-byte signatures.</summary>
public static class Ed25519
{
    public const int KeySize = 32;
    public const int SignatureSize = 64;

    /// <summary>A new key pair from a cryptographic random generator.</summary>
    public static (byte[] PrivateKey, byte[] PublicKey) GenerateKeyPair()
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        return (privateKey.GetEncoded(), privateKey.GeneratePublicKey().GetEncoded());
    }

    public static byte[] PublicKeyOf(byte[] privateKey) => new Ed25519PrivateKeyParameters(privateKey).GeneratePublicKey().GetEncoded();

    public static byte[] Sign(byte[] privateKey, ReadOnlySpan<byte> data)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKey));
        signer.BlockUpdate(data.ToArray(), 0, data.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(byte[] publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != KeySize || signature.Length != SignatureSize)
            return false;
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey));
        verifier.BlockUpdate(data.ToArray(), 0, data.Length);
        return verifier.VerifySignature(signature.ToArray());
    }
}
