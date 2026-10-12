namespace One4All.Licensing.Core;

/// <summary>
/// The licence's feature flags (brief §11.3 "features": […]) and which an edition carries by default. A licence that lists no features at all was
/// issued before feature flags and keeps every feature; one that lists some has only those.
/// </summary>
public static class LicenceFeatures
{
    /// <summary>Documents and journals in currencies other than the base currency.</summary>
    public const string MultiCurrency = "MULTI_CURRENCY";

    /// <summary>The till selling (and opening) while the database cannot be reached.</summary>
    public const string OfflinePos = "OFFLINE_POS";

    /// <summary>Integration clients calling the API with their keys.</summary>
    public const string ApiAccess = "API_ACCESS";

    /// <summary>Consolidated and translated trial balances across companies.</summary>
    public const string Consolidation = "CONSOLIDATION";

    /// <summary>IAS 29 restatement for hyperinflationary economies.</summary>
    public const string Hyperinflation = "HYPERINFLATION";

    /// <summary>Every feature flag.</summary>
    public static IReadOnlyList<string> All { get; } = [MultiCurrency, OfflinePos, ApiAccess, Consolidation, Hyperinflation];

    /// <summary>The features an edition carries unless the licence says otherwise.</summary>
    /// <param name="edition">The edition.</param>
    /// <returns>The feature flags.</returns>
    public static IReadOnlyList<string> ForEdition(LicenceEdition edition) => edition switch
    {
        LicenceEdition.Standard => [MultiCurrency, OfflinePos],
        LicenceEdition.Professional => [MultiCurrency, OfflinePos, ApiAccess],
        _ => All,
    };

    /// <summary>Whether a feature may be used.</summary>
    /// <param name="enforced">Whether the licence is enforced on this host.</param>
    /// <param name="licensed">The features the licence lists (null: no licence payload).</param>
    /// <param name="feature">The feature asked for.</param>
    /// <returns>True when not enforced, the licence predates feature flags (lists none), or it lists the feature.</returns>
    public static bool Allows(bool enforced, IReadOnlyCollection<string>? licensed, string feature)
        => !enforced || licensed is null || licensed.Count == 0 || licensed.Contains(feature, StringComparer.OrdinalIgnoreCase);
}
