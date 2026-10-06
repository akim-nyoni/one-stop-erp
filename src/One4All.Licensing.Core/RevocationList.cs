using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace One4All.Licensing.Core;

/// <summary>
/// The vendor's list of revoked licence ids (brief §11.4), signed with the licensing key so the ERP can trust it offline. A later list
/// (higher <see cref="Sequence"/>) replaces an earlier one; the ERP refuses to go back to an older list.
/// </summary>
public sealed class RevocationList
{
    /// <summary>Gets or sets the signing key id.</summary>
    [JsonPropertyName("kid")]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the list's sequence number (increases with every list issued).</summary>
    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }

    /// <summary>Gets or sets when it was issued.</summary>
    [JsonPropertyName("issued_at")]
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Gets or sets the revoked licence ids.</summary>
    [JsonPropertyName("revoked")]
    public List<string> Revoked { get; set; } = [];
}

/// <summary>Signs and verifies revocation lists: the token <c>RL1.payload_base64url.signature_base64url</c>.</summary>
public static class RevocationListCodec
{
    private const string Prefix = "RL1.";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Signs a list.</summary>
    /// <param name="list">The list (its ids are sorted and de-duplicated first).</param>
    /// <param name="privateKeyBase64">The vendor's Ed25519 private key.</param>
    /// <returns>The token.</returns>
    public static string Sign(RevocationList list, string privateKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Revoked = [.. list.Revoked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(list, Options);
        Ed25519Signer signer = new();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        return $"{Prefix}{Base64Url.Encode(payload)}.{Base64Url.Encode(signer.GenerateSignature())}";
    }

    /// <summary>Verifies a token against the trusted keys.</summary>
    /// <param name="token">The token.</param>
    /// <param name="trustedPublicKeysByKid">The trusted public keys by key id.</param>
    /// <returns>The list, or null with why it is not acceptable.</returns>
    public static (RevocationList? List, string? Error) Verify(string token, IReadOnlyDictionary<string, byte[]> trustedPublicKeysByKid)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKeysByKid);
        string text = (token ?? string.Empty).Trim();
        string[] parts = text.StartsWith(Prefix, StringComparison.Ordinal) ? text[Prefix.Length..].Split('.') : [];
        if (parts.Length != 2)
        {
            return (null, "Not a revocation list (expected RL1.payload.signature).");
        }

        try
        {
            byte[] payload = Base64Url.Decode(parts[0]);
            RevocationList? list = JsonSerializer.Deserialize<RevocationList>(Encoding.UTF8.GetString(payload), Options);
            if (list is null || !trustedPublicKeysByKid.TryGetValue(list.KeyId, out byte[]? key))
            {
                return (null, $"The revocation list is not signed by a trusted key ('{list?.KeyId}').");
            }

            Ed25519Signer verifier = new();
            verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(key, 0));
            verifier.BlockUpdate(payload, 0, payload.Length);
            return verifier.VerifySignature(Base64Url.Decode(parts[1])) ? (list, null) : (null, "The revocation list's signature does not match.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return (null, $"The revocation list cannot be read: {ex.Message}");
        }
    }
}

/// <summary>
/// The offline activation request a customer site sends the vendor (brief §11.4): its machine fingerprint, the customer reference and when
/// it was made, as the copyable code <c>O4A-REQ-…</c>. The vendor pastes it into the generator, which binds the licence to that machine.
/// </summary>
/// <param name="Fingerprint">The machine fingerprint (<c>sha256:…</c>).</param>
/// <param name="CustomerReference">The customer's reference, if known.</param>
/// <param name="Site">The site or machine name.</param>
/// <param name="RequestedAt">When the code was made.</param>
public sealed record LicenceRequestCode(string Fingerprint, string? CustomerReference, string? Site, DateTimeOffset RequestedAt)
{
    private const string Prefix = "O4A-REQ-";

    /// <summary>Encodes the request as its code.</summary>
    /// <returns>The code.</returns>
    public string Encode() => Prefix + Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(this));

    /// <summary>Reads a request code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The request, or null when it is not a valid code (including a fingerprint that does not parse).</returns>
    public static LicenceRequestCode? TryParse(string? code)
    {
        string text = (code ?? string.Empty).Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            LicenceRequestCode? request = JsonSerializer.Deserialize<LicenceRequestCode>(Base64Url.Decode(text[Prefix.Length..]));
            return request is not null && MachineFingerprint.Parse(request.Fingerprint).ComponentHashes.Count > 0 ? request : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
