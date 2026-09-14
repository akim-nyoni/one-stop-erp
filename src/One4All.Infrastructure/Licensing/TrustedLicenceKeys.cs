using One4All.Application.Abstractions.Licensing;

namespace One4All.Infrastructure.Licensing;

/// <summary>Configuration carrying the trusted vendor public keys (Base64), keyed by <c>kid</c>.</summary>
public sealed class LicensingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Licensing";

    /// <summary>Map of key id to Base64-encoded Ed25519 public key.</summary>
    public Dictionary<string, string> TrustedKeys { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Reads the trusted licence public keys from <see cref="LicensingOptions"/>.</summary>
public sealed class TrustedLicenceKeys : ITrustedLicenceKeys
{
    /// <summary>Creates the provider from options.</summary>
    /// <param name="options">The licensing options.</param>
    public TrustedLicenceKeys(LicensingOptions options) =>
        PublicKeysByKid = options.TrustedKeys.ToDictionary(
            kv => kv.Key,
            kv => Convert.FromBase64String(kv.Value),
            StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, byte[]> PublicKeysByKid { get; }
}
