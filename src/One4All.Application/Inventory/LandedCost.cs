namespace One4All.Application.Inventory;

/// <summary>How landed charges are shared across the lines of a goods receipt.</summary>
public enum LandedCostBasis
{
    /// <summary>In proportion to each line's purchase value.</summary>
    Value = 1,

    /// <summary>In proportion to each line's quantity.</summary>
    Quantity = 2,

    /// <summary>In proportion to each line's weight (quantity times the item's unit weight), as freight is often charged.</summary>
    Weight = 3,

    /// <summary>In proportion to each line's volume (quantity times the item's unit volume), as sea freight is often charged.</summary>
    Volume = 4,
}

/// <summary>A goods-receipt line to share landed charges over.</summary>
/// <param name="Quantity">The quantity received.</param>
/// <param name="Value">Its purchase value.</param>
/// <param name="Weight">Its total weight.</param>
/// <param name="Volume">Its total volume.</param>
public readonly record struct LandedCostLine(decimal Quantity, decimal Value, decimal Weight = 0m, decimal Volume = 0m);

/// <summary>
/// Apportions landed charges (freight, duty, clearing, insurance) across a goods receipt (brief §3.6). The shares are
/// rounded to cents and always add up to the charge exactly (largest remainder). Pure and unit-tested.
/// </summary>
public static class LandedCost
{
    /// <summary>Shares a charge across the lines.</summary>
    /// <param name="lines">The receipt lines.</param>
    /// <param name="charge">The total charge.</param>
    /// <param name="basis">The apportionment basis.</param>
    /// <returns>Each line's share, in line order.</returns>
    /// <exception cref="ArgumentException">Thrown when the lines carry nothing to apportion by.</exception>
    public static decimal[] Apportion(IReadOnlyList<LandedCostLine> lines, decimal charge, LandedCostBasis basis)
    {
        ArgumentNullException.ThrowIfNull(lines);
        decimal[] weights = [.. lines.Select(l => basis switch
        {
            LandedCostBasis.Quantity => l.Quantity,
            LandedCostBasis.Weight => l.Weight,
            LandedCostBasis.Volume => l.Volume,
            _ => l.Value,
        })];
        decimal total = weights.Sum();
        if (lines.Count == 0 || total <= 0m)
        {
            throw new ArgumentException("There is nothing to apportion the charge over.", nameof(lines));
        }

        // Exact shares, floored to cents; the cents left over go to the largest remainders.
        decimal[] exact = [.. weights.Select(w => charge * w / total)];
        decimal[] shares = [.. exact.Select(e => Math.Floor(e * 100m) / 100m)];
        int leftover = (int)decimal.Round((charge - shares.Sum()) * 100m, 0, MidpointRounding.AwayFromZero);
        foreach (int i in exact.Select((e, i) => (Remainder: e - shares[i], Index: i)).OrderByDescending(x => x.Remainder).ThenBy(x => x.Index).Take(leftover).Select(x => x.Index))
        {
            shares[i] += 0.01m;
        }

        return shares;
    }
}
