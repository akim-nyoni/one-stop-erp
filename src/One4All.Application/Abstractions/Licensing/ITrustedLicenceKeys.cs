namespace One4All.Application.Abstractions.Licensing;

/// <summary>
/// Supplies the vendor public keys the ERP trusts to sign licences, keyed by <c>kid</c> (brief §11.5).
/// These are embedded in / configured for the deployment; the private counterparts live only in the
/// vendor Generator.
/// </summary>
public interface ITrustedLicenceKeys
{
    /// <summary>The trusted public keys as raw bytes, keyed by key id.</summary>
    IReadOnlyDictionary<string, byte[]> PublicKeysByKid { get; }
}
