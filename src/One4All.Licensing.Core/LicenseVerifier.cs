using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace One4All.Licensing.Core;

/// <summary>The outcome of verifying a licence token's signature.</summary>
/// <param name="IsValid">True when the token is well-formed and its signature checks out against a trusted key.</param>
/// <param name="Payload">The decoded payload when valid; otherwise null.</param>
/// <param name="Error">A description of why verification failed, when invalid.</param>
public readonly record struct SignatureVerification(bool IsValid, LicensePayload? Payload, string? Error);

/// <summary>
/// Verifies a licence token's Ed25519 signature against a set of trusted public keys (one per <c>kid</c>,
/// enabling key rotation). The ERP embeds these keys and verifies fully offline (brief §11.2, §11.5).
/// This checks the <em>signature</em> only; date/fingerprint/limit checks are done by
/// <see cref="LicenseValidator"/>.
/// </summary>
public sealed class LicenseVerifier
{
    private readonly IReadOnlyDictionary<string, byte[]> _trustedKeysByKid;

    /// <summary>Creates a verifier from trusted public keys keyed by their key id.</summary>
    /// <param name="trustedPublicKeysByKid">Map of <c>kid</c> to raw Ed25519 public key bytes.</param>
    public LicenseVerifier(IReadOnlyDictionary<string, byte[]> trustedPublicKeysByKid) =>
        _trustedKeysByKid = trustedPublicKeysByKid;

    /// <summary>Creates a verifier trusting a single Base64 public key under a given kid.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="publicKeyBase64">The Base64 public key.</param>
    /// <returns>A verifier.</returns>
    public static LicenseVerifier ForSingleKey(string keyId, string publicKeyBase64) =>
        new(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [keyId] = Convert.FromBase64String(publicKeyBase64),
        });

    /// <summary>Verifies a compact licence token.</summary>
    /// <param name="token">The <c>payload.signature</c> token.</param>
    /// <returns>The verification result.</returns>
    public SignatureVerification Verify(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new SignatureVerification(false, null, "Empty token.");
        }

        string[] parts = token.Trim().Split('.');
        if (parts.Length != 2)
        {
            return new SignatureVerification(false, null, "Malformed token (expected payload.signature).");
        }

        byte[] canonical;
        byte[] signature;
        try
        {
            canonical = Base64Url.Decode(parts[0]);
            signature = Base64Url.Decode(parts[1]);
        }
        catch (FormatException)
        {
            return new SignatureVerification(false, null, "Token is not valid Base64Url.");
        }

        LicensePayload? payload;
        try
        {
            payload = CanonicalJson.Deserialize(Encoding.UTF8.GetString(canonical));
        }
        catch (System.Text.Json.JsonException)
        {
            return new SignatureVerification(false, null, "Payload is not valid JSON.");
        }

        if (payload is null)
        {
            return new SignatureVerification(false, null, "Payload could not be decoded.");
        }

        if (!_trustedKeysByKid.TryGetValue(payload.KeyId, out byte[]? publicKey))
        {
            return new SignatureVerification(false, null, $"Untrusted signing key '{payload.KeyId}'.");
        }

        Ed25519Signer verifier = new();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(canonical, 0, canonical.Length);

        return verifier.VerifySignature(signature)
            ? new SignatureVerification(true, payload, null)
            : new SignatureVerification(false, null, "Signature does not match (licence tampered or wrong key).");
    }
}
