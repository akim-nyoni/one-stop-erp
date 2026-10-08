using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace One4All.Licensing.Core;

/// <summary>
/// A one-off permission signed with the licence holder's (vendor's) key for an operation no user may perform alone (brief §5.1: audit
/// "retention/archival via a partition-detach process requiring the licence-holder's key"): what may be done, to what, and until when.
/// </summary>
public sealed class MaintenanceAuthorisation
{
    /// <summary>Gets or sets the signing key's id.</summary>
    [JsonPropertyName("kid")]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the operation (e.g. <c>audit.archive</c>).</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets what it applies to (e.g. the year <c>2021</c>).</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the installation it was issued for (the licence id; blank: any).</summary>
    [JsonPropertyName("licence")]
    public string? LicenceId { get; set; }

    /// <summary>Gets or sets when it was issued.</summary>
    [JsonPropertyName("issued_at")]
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Gets or sets when it stops being valid.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Signs and checks maintenance authorisations (<c>MA1.payload.signature</c>, Ed25519 like licences and revocation lists).</summary>
public static class MaintenanceAuthorisationCodec
{
    private const string Prefix = "MA1.";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Signs an authorisation with the licence holder's private key.</summary>
    /// <param name="authorisation">The authorisation.</param>
    /// <param name="privateKeyBase64">The Ed25519 private key.</param>
    /// <returns>The token.</returns>
    public static string Sign(MaintenanceAuthorisation authorisation, string privateKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(authorisation);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(authorisation, Options);
        Ed25519Signer signer = new();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        return $"{Prefix}{Base64Url.Encode(payload)}.{Base64Url.Encode(signer.GenerateSignature())}";
    }

    /// <summary>Checks a token: signed by a trusted key, for this action and scope, not expired, and (when it names one) for this licence.</summary>
    /// <param name="token">The token.</param>
    /// <param name="trustedPublicKeysByKid">The trusted public keys.</param>
    /// <param name="action">The operation about to be performed.</param>
    /// <param name="scope">What it is about to be performed on.</param>
    /// <param name="now">The time now.</param>
    /// <param name="licenceId">This installation's licence id, if known.</param>
    /// <returns>The authorisation, or why it does not allow the operation.</returns>
    public static (MaintenanceAuthorisation? Authorisation, string? Error) Verify(string token, IReadOnlyDictionary<string, byte[]> trustedPublicKeysByKid, string action, string scope,
        DateTimeOffset now, string? licenceId = null)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKeysByKid);
        string text = (token ?? string.Empty).Trim();
        string[] parts = text.StartsWith(Prefix, StringComparison.Ordinal) ? text[Prefix.Length..].Split('.') : [];
        if (parts.Length != 2)
        {
            return (null, "Not a maintenance authorisation (expected MA1.payload.signature).");
        }

        try
        {
            byte[] payload = Base64Url.Decode(parts[0]);
            MaintenanceAuthorisation? authorisation = JsonSerializer.Deserialize<MaintenanceAuthorisation>(Encoding.UTF8.GetString(payload), Options);
            if (authorisation is null || !trustedPublicKeysByKid.TryGetValue(authorisation.KeyId, out byte[]? key))
            {
                return (null, $"The authorisation is not signed by a trusted key ('{authorisation?.KeyId}').");
            }

            Ed25519Signer verifier = new();
            verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(key, 0));
            verifier.BlockUpdate(payload, 0, payload.Length);
            if (!verifier.VerifySignature(Base64Url.Decode(parts[1])))
            {
                return (null, "The authorisation's signature does not match.");
            }

            if (!string.Equals(authorisation.Action, action, StringComparison.Ordinal) || !string.Equals(authorisation.Scope, scope, StringComparison.Ordinal))
            {
                return (null, $"The authorisation is for {authorisation.Action} {authorisation.Scope}, not {action} {scope}.");
            }

            if (now > authorisation.ExpiresAt)
            {
                return (null, $"The authorisation expired on {authorisation.ExpiresAt:yyyy-MM-dd}.");
            }

            return !string.IsNullOrEmpty(authorisation.LicenceId) && licenceId is not null && !string.Equals(authorisation.LicenceId, licenceId, StringComparison.Ordinal)
                ? (null, "The authorisation was issued for another installation.")
                : (authorisation, null);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return (null, $"The authorisation cannot be read: {ex.Message}");
        }
    }
}
