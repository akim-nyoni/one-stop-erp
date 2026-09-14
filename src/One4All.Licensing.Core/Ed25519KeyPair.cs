using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace One4All.Licensing.Core;

/// <summary>
/// An Ed25519 key pair. The vendor <c>Generator</c> holds the private key and issues licences; the ERP
/// embeds only the public key(s) and verifies offline (brief §11.2). Keys are exchanged as Base64 of
/// their raw 32-byte encodings.
/// </summary>
public sealed class Ed25519KeyPair
{
    private Ed25519KeyPair(byte[] privateKey, byte[] publicKey)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
    }

    /// <summary>The raw 32-byte private key seed.</summary>
    public byte[] PrivateKey { get; }

    /// <summary>The raw 32-byte public key.</summary>
    public byte[] PublicKey { get; }

    /// <summary>The private key as Base64.</summary>
    public string PrivateKeyBase64 => Convert.ToBase64String(PrivateKey);

    /// <summary>The public key as Base64.</summary>
    public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);

    /// <summary>Generates a new random key pair.</summary>
    /// <returns>The generated pair.</returns>
    public static Ed25519KeyPair Generate()
    {
        Ed25519KeyPairGenerator generator = new();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();

        byte[] priv = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        byte[] pub = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        return new Ed25519KeyPair(priv, pub);
    }

    /// <summary>Reconstructs a key pair from a stored private key (the public key is derived from it).</summary>
    /// <param name="privateKeyBase64">The Base64 private key.</param>
    /// <returns>The key pair.</returns>
    public static Ed25519KeyPair FromPrivateKey(string privateKeyBase64)
    {
        byte[] priv = Convert.FromBase64String(privateKeyBase64);
        Ed25519PrivateKeyParameters parameters = new(priv, 0);
        byte[] pub = parameters.GeneratePublicKey().GetEncoded();
        return new Ed25519KeyPair(priv, pub);
    }
}
