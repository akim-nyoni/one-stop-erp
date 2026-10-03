using One4All.Core.Accounting;

namespace One4All.Application.Accounting;

/// <summary>
/// Shares an overhead pool across receivers by their drivers: each share is rounded to the cent and the shares always
/// add up to the pool exactly (largest remainder), for debit and credit pools alike. Pure and unit-tested.
/// </summary>
public static class OverheadAllocation
{
    /// <summary>Checks a rule's drivers against its basis.</summary>
    /// <param name="basis">The basis.</param>
    /// <param name="drivers">Each receiver's driver: a percentage, headcount, area or usage quantity.</param>
    /// <returns>Null when valid, else why not.</returns>
    public static string? Validate(AllocationBasis basis, IReadOnlyList<decimal> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        if (drivers.Count == 0)
        {
            return "An allocation rule needs at least one receiving cost centre.";
        }

        if (drivers.Any(d => d < 0m))
        {
            return "Allocation drivers cannot be negative.";
        }

        if (basis == AllocationBasis.FixedPercent && drivers.Sum() != 100m)
        {
            return $"Fixed percentages must add up to 100, not {drivers.Sum():0.####}.";
        }

        return drivers.Sum() <= 0m ? "The allocation drivers add up to nothing." : null;
    }

    /// <summary>Shares a pool by the drivers.</summary>
    /// <param name="pool">The amount to share; negative for a credit pool.</param>
    /// <param name="drivers">Each receiver's driver.</param>
    /// <returns>Each receiver's share, in driver order.</returns>
    /// <exception cref="ArgumentException">Thrown when the drivers add up to nothing.</exception>
    public static decimal[] Share(decimal pool, IReadOnlyList<decimal> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        decimal total = drivers.Sum();
        if (drivers.Count == 0 || total <= 0m)
        {
            throw new ArgumentException("There is nothing to share the pool by.", nameof(drivers));
        }

        // Share the absolute amount (so flooring works the same way for credit pools), then restore the sign.
        decimal amount = Math.Abs(pool);
        decimal[] exact = [.. drivers.Select(d => amount * d / total)];
        decimal[] shares = [.. exact.Select(e => Math.Floor(e * 100m) / 100m)];
        int leftover = (int)decimal.Round((amount - shares.Sum()) * 100m, 0, MidpointRounding.AwayFromZero);
        foreach (int i in exact.Select((e, i) => (Remainder: e - shares[i], Index: i)).OrderByDescending(x => x.Remainder).ThenBy(x => x.Index).Take(leftover).Select(x => x.Index))
        {
            shares[i] += 0.01m;
        }

        return pool < 0m ? [.. shares.Select(s => -s)] : shares;
    }
}
