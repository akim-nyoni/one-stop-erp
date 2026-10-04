using One4All.Core.Municipal;

namespace One4All.Application.Municipal;

/// <summary>A tariff version offered for a service, for matching.</summary>
/// <param name="TariffId">The tariff.</param>
/// <param name="Code">Its code.</param>
/// <param name="Category">The consumer category it applies to.</param>
/// <param name="Ward">The ward it applies to, or null for the whole council.</param>
/// <param name="EffectiveFrom">When it takes effect.</param>
public readonly record struct TariffCandidate(long TariffId, string Code, ConsumerCategory Category, string? Ward, DateOnly EffectiveFrom);

/// <summary>
/// Chooses the tariff that applies to a consumer (brief §4.9: tariffs vary by category and ward). The category must
/// match exactly; a ward-specific tariff beats a council-wide one; and the latest version in force on the date wins.
/// Pure and unit-tested.
/// </summary>
public static class TariffCatalogue
{
    /// <summary>The tariff that applies, or null when none does.</summary>
    /// <param name="candidates">The active tariff versions for the service.</param>
    /// <param name="category">The consumer's category.</param>
    /// <param name="ward">The consumer's ward.</param>
    /// <param name="date">The date it is needed for.</param>
    /// <returns>The chosen candidate, or null.</returns>
    public static TariffCandidate? Best(IEnumerable<TariffCandidate> candidates, ConsumerCategory category, string? ward, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(c => c.Category == category && c.EffectiveFrom <= date && (c.Ward == null || string.Equals(c.Ward, ward, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Ward != null)         // a ward-specific tariff beats a council-wide one
            .ThenByDescending(c => c.EffectiveFrom)          // then the latest version in force
            .ThenBy(c => c.Code, StringComparer.Ordinal)     // stable tie-break
            .Select(c => (TariffCandidate?)c)
            .FirstOrDefault();
    }
}
