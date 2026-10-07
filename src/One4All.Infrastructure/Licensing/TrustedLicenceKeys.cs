using One4All.Application.Abstractions.Licensing;

namespace One4All.Infrastructure.Licensing;

/// <summary>Configuration carrying the trusted vendor public keys (Base64), keyed by <c>kid</c>.</summary>
public sealed class LicensingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Licensing";

    /// <summary>Map of key id to Base64-encoded Ed25519 public key.</summary>
    public Dictionary<string, string> TrustedKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether the licence is enforced: posting refused unless it is usable, unlicensed API modules
    /// refused, limits applied. The server and desktop enforce; embedded test hosts may not.
    /// </summary>
    public bool Enforce { get; set; }

    /// <summary>
    /// Gets or sets whether clock rollback is detected (brief §11.5: a high-water mark kept in the database and a file); on by default wherever
    /// the licence is enforced.
    /// </summary>
    public bool? DetectClockRollback { get; set; }

    /// <summary>Gets or sets where the file copy of the clock mark lives (default: the common application data folder).</summary>
    public string? ClockMarkPath { get; set; }
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
