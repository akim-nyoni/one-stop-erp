namespace One4All.Application.Housing;

/// <summary>A unit's floor area.</summary>
/// <param name="UnitNumber">The unit.</param>
/// <param name="FloorArea">Its floor area in m².</param>
public sealed record UnitArea(string UnitNumber, decimal FloorArea);

/// <summary>An amount apportioned to a unit.</summary>
/// <param name="UnitNumber">The unit.</param>
/// <param name="Amount">The amount.</param>
public sealed record UnitAmount(string UnitNumber, decimal Amount);

/// <summary>
/// Sectional title arithmetic (brief §4.10.6): each unit's participation quota is its floor area's share of the total, to
/// four decimals, with any rounding on the largest unit so the quotas total exactly 100%; and a body corporate levy is
/// split by quota to the cent, the rounding again on the largest unit so the split equals the levy. Pure and unit-tested.
/// </summary>
public static class SectionalQuotas
{
    /// <summary>The participation quotas, in the order given.</summary>
    /// <param name="units">The units and their floor areas.</param>
    /// <returns>Each unit's quota (%).</returns>
    public static IReadOnlyList<UnitAmount> FromFloorAreas(IReadOnlyList<UnitArea> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        decimal total = units.Sum(u => u.FloorArea);
        if (units.Count == 0 || total <= 0m || units.Any(u => u.FloorArea <= 0m))
        {
            throw new ArgumentException("Every unit needs a positive floor area.", nameof(units));
        }

        return Apportion(units.Select(u => (u.UnitNumber, u.FloorArea)).ToList(), 100m, 4);
    }

    /// <summary>A levy split by quota.</summary>
    /// <param name="quotas">The units' quotas.</param>
    /// <param name="levy">The levy for the period.</param>
    /// <returns>Each unit's share, in the order given.</returns>
    public static IReadOnlyList<UnitAmount> SplitLevy(IReadOnlyList<UnitAmount> quotas, decimal levy)
    {
        ArgumentNullException.ThrowIfNull(quotas);
        return Apportion(quotas.Select(q => (q.UnitNumber, q.Amount)).ToList(), levy, 2);
    }

    private static List<UnitAmount> Apportion(List<(string Unit, decimal Weight)> weights, decimal total, int decimals)
    {
        decimal sum = weights.Sum(w => w.Weight);
        List<UnitAmount> shares = [.. weights.Select(w => new UnitAmount(w.Unit, Math.Round(total * w.Weight / sum, decimals, MidpointRounding.AwayFromZero)))];
        int largest = weights.IndexOf(weights.OrderByDescending(w => w.Weight).ThenBy(w => w.Unit, StringComparer.Ordinal).First());
        shares[largest] = shares[largest] with { Amount = shares[largest].Amount + (total - shares.Sum(s => s.Amount)) };
        return shares;
    }
}
