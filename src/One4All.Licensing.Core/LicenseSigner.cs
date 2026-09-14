using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace One4All.Licensing.Core;

/// <summary>
/// Signs a licence payload with an Ed25519 private key, producing the compact token
/// <c>payload_base64url.signature_base64url</c> (brief §11.2). Vendor-only — the private key never
/// ships with the ERP.
/// </summary>
public sealed class LicenseSigner
{
    private readonly Ed25519PrivateKeyParameters _privateKey;

    /// <summary>Creates a signer from a Base64 private key.</summary>
    /// <param name="privateKeyBase64">The Base64-encoded Ed25519 private key.</param>
    public LicenseSigner(string privateKeyBase64)
        => _privateKey = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0);

    /// <summary>Signs a payload and returns the compact licence token.</summary>
    /// <param name="payload">The payload to sign.</param>
    /// <returns>The signed token string.</returns>
    public string Sign(LicensePayload payload)
    {
        byte[] canonical = CanonicalJson.SerializeToBytes(payload);

        Ed25519Signer signer = new();
        signer.Init(forSigning: true, _privateKey);
        signer.BlockUpdate(canonical, 0, canonical.Length);
        byte[] signature = signer.GenerateSignature();

        return $"{Base64Url.Encode(canonical)}.{Base64Url.Encode(signature)}";
    }
}
