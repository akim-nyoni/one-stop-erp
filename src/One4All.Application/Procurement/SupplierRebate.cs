namespace One4All.Application.Procurement;

/// <summary>A rebate tier: once spend reaches the threshold, the rate applies to all of it.</summary>
/// <param name="Threshold">The spend from which the tier applies.</param>
/// <param name="RatePercent">The rebate rate.</param>
public readonly record struct RebateTier(decimal Threshold, decimal RatePercent);

/// <summary>
/// Supplier volume rebates (brief §4.3) on a retrospective tiered scale: the highest tier reached sets the rate for all
/// spend in the agreement period, so crossing a tier lifts the rebate on what was bought before. Pure and unit-tested.
/// </summary>
public static class SupplierRebate
{
    /// <summary>The rate for a level of spend.</summary>
    /// <param name="spend">The spend so far.</param>
    /// <param name="tiers">The tiers.</param>
    /// <returns>The rate of the highest tier reached, or 0 below the first.</returns>
    public static decimal RateFor(decimal spend, IReadOnlyList<RebateTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        return tiers.Where(t => spend >= t.Threshold).Select(t => t.RatePercent).DefaultIfEmpty(0m).Max();
    }

    /// <summary>The rebate earned on a level of spend.</summary>
    /// <param name="spend">The spend so far.</param>
    /// <param name="tiers">The tiers.</param>
    /// <returns>The rebate, to the cent.</returns>
    public static decimal Earned(decimal spend, IReadOnlyList<RebateTier> tiers)
        => spend <= 0m ? 0m : decimal.Round(spend * RateFor(spend, tiers) / 100m, 2, MidpointRounding.AwayFromZero);
}
