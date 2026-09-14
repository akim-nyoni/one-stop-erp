using System.Security.Cryptography;
using System.Text;

namespace One4All.Licensing.Core;

/// <summary>
/// A machine fingerprint built from several stable hardware/OS components (brief §11.5: motherboard
/// UUID, machine GUID, primary NIC MAC, volume serial). Each component is stored as its own hash so
/// binding can tolerate one component changing (e.g. a replaced NIC): a candidate matches an allowed
/// fingerprint when at least <see cref="DefaultMinMatchingComponents"/> of the components are equal.
///
/// The platform-specific gathering of raw components is supplied by an <c>IMachineIdProvider</c>
/// implemented in the host; this type only hashes and compares.
/// </summary>
public sealed class MachineFingerprint
{
    private const char Separator = ':';

    /// <summary>The default number of components that must match for a fuzzy match (3 of 4).</summary>
    public const int DefaultMinMatchingComponents = 3;

    private MachineFingerprint(IReadOnlyList<string> componentHashes) => ComponentHashes = componentHashes;

    /// <summary>The per-component hashes.</summary>
    public IReadOnlyList<string> ComponentHashes { get; }

    /// <summary>
    /// Builds a fingerprint from raw component values (empty/whitespace components are ignored). Each is
    /// individually hashed with SHA-256 so the raw values never leave the machine.
    /// </summary>
    /// <param name="rawComponents">The stable raw component strings.</param>
    /// <returns>The fingerprint.</returns>
    public static MachineFingerprint FromComponents(IEnumerable<string> rawComponents)
    {
        List<string> hashes = rawComponents
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(HashComponent)
            .ToList();
        return new MachineFingerprint(hashes);
    }

    /// <summary>Parses a serialized fingerprint (colon-separated component hashes).</summary>
    /// <param name="serialized">The serialized form.</param>
    /// <returns>The fingerprint.</returns>
    public static MachineFingerprint Parse(string serialized)
    {
        string value = serialized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? serialized["sha256:".Length..]
            : serialized;
        return new MachineFingerprint(value.Split(Separator, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Serializes to <c>sha256:</c>-prefixed colon-separated component hashes.</summary>
    /// <returns>The serialized fingerprint.</returns>
    public override string ToString() => "sha256:" + string.Join(Separator, ComponentHashes);

    /// <summary>Counts how many components this fingerprint shares with another.</summary>
    /// <param name="other">The other fingerprint.</param>
    /// <returns>The number of equal components.</returns>
    public int MatchingComponentCount(MachineFingerprint other)
    {
        HashSet<string> mine = new(ComponentHashes, StringComparer.OrdinalIgnoreCase);
        return other.ComponentHashes.Count(h => mine.Contains(h));
    }

    /// <summary>Returns true when this fingerprint fuzzily matches <paramref name="other"/>.</summary>
    /// <param name="other">The other fingerprint.</param>
    /// <param name="minMatching">Minimum matching components required.</param>
    /// <returns>True when enough components match.</returns>
    public bool FuzzyMatches(MachineFingerprint other, int minMatching = DefaultMinMatchingComponents) =>
        MatchingComponentCount(other) >= Math.Min(minMatching, Math.Min(ComponentHashes.Count, other.ComponentHashes.Count));

    private static string HashComponent(string raw)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim().ToUpperInvariant()));
        return Convert.ToHexStringLower(bytes)[..16]; // 64-bit prefix per component keeps the fingerprint compact.
    }
}

/// <summary>
/// Supplies the raw, stable machine components used to build a <see cref="MachineFingerprint"/>. Implemented
/// per platform in the host (e.g. WMI/registry on Windows).
/// </summary>
public interface IMachineIdProvider
{
    /// <summary>Gathers the raw component strings for this machine.</summary>
    /// <returns>The raw components (order-independent).</returns>
    IReadOnlyList<string> GetComponents();
}
