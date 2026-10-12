using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace One4All.Licensing.Core;

/// <summary>
/// A release on the update channel (brief §2 packaging "auto-update channel"), signed with the vendor's key like licences: the version, where the
/// installer is, its SHA-256 and the oldest version still allowed to run.
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>Gets or sets the signing key's id.</summary>
    [JsonPropertyName("kid")]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the version released (e.g. 1.4.0).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets where the installer is (https URL or a path on the network share).</summary>
    [JsonPropertyName("installer")]
    public string InstallerLocation { get; set; } = string.Empty;

    /// <summary>Gets or sets the installer's SHA-256 (hex).</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Gets or sets the oldest version still allowed to run (older ones must update), or null.</summary>
    [JsonPropertyName("minimum")]
    public string? MinimumVersion { get; set; }

    /// <summary>Gets or sets the release notes.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Gets or sets when it was published.</summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }
}

/// <summary>Whether this installation should update.</summary>
public enum UpdateStatus
{
    /// <summary>This is the latest release (or newer).</summary>
    UpToDate = 0,

    /// <summary>A newer release is available.</summary>
    Available = 1,

    /// <summary>This version is older than the minimum: it must update.</summary>
    Required = 2,
}

/// <summary>Signs and checks update manifests (<c>UP1.payload.signature</c>, Ed25519), and decides whether to update. Pure.</summary>
public static class UpdateManifestCodec
{
    private const string Prefix = "UP1.";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Signs a manifest with the vendor's private key.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="privateKeyBase64">The Ed25519 private key.</param>
    /// <returns>The token.</returns>
    public static string Sign(UpdateManifest manifest, string privateKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
        Ed25519Signer signer = new();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        return $"{Prefix}{Base64Url.Encode(payload)}.{Base64Url.Encode(signer.GenerateSignature())}";
    }

    /// <summary>Checks a manifest token is signed by a trusted key.</summary>
    /// <param name="token">The token.</param>
    /// <param name="trustedPublicKeysByKid">The trusted public keys.</param>
    /// <returns>The manifest, or why it is not trusted.</returns>
    public static (UpdateManifest? Manifest, string? Error) Verify(string token, IReadOnlyDictionary<string, byte[]> trustedPublicKeysByKid)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKeysByKid);
        string text = (token ?? string.Empty).Trim();
        string[] parts = text.StartsWith(Prefix, StringComparison.Ordinal) ? text[Prefix.Length..].Split('.') : [];
        if (parts.Length != 2)
        {
            return (null, "Not an update manifest (expected UP1.payload.signature).");
        }

        try
        {
            byte[] payload = Base64Url.Decode(parts[0]);
            UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(Encoding.UTF8.GetString(payload), Options);
            if (manifest is null || !trustedPublicKeysByKid.TryGetValue(manifest.KeyId, out byte[]? key))
            {
                return (null, $"The update is not signed by a trusted key ('{manifest?.KeyId}').");
            }

            Ed25519Signer verifier = new();
            verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(key, 0));
            verifier.BlockUpdate(payload, 0, payload.Length);
            return verifier.VerifySignature(Base64Url.Decode(parts[1])) ? (manifest, null) : (null, "The update's signature does not match.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return (null, "The update manifest is damaged.");
        }
    }

    /// <summary>Whether the running version should update to a release.</summary>
    /// <param name="current">The running version.</param>
    /// <param name="manifest">The release.</param>
    /// <returns>Up to date, available, or required (the running version is older than the minimum).</returns>
    public static UpdateStatus Decide(Version current, UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(manifest);
        static Version Parse(string? text) => System.Version.TryParse(text, out Version? v) ? Normalise(v) : new Version(0, 0, 0, 0);
        static Version Normalise(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        Version running = Normalise(current);
        if (manifest.MinimumVersion is not null && running < Parse(manifest.MinimumVersion))
        {
            return UpdateStatus.Required;
        }

        return running < Parse(manifest.Version) ? UpdateStatus.Available : UpdateStatus.UpToDate;
    }
}
