namespace One4All.Application.Housing;

/// <summary>A scheme cost element's budget against actual.</summary>
/// <param name="Variance">Budget less actual (negative when overspent).</param>
/// <param name="UsedPercent">Actual as a percentage of budget, one decimal (null with no budget).</param>
public sealed record BudgetComparison(decimal Variance, decimal? UsedPercent);

/// <summary>
/// Scheme cost rules (brief §14 H84): a capitalised cost is spread equally over the stands, the cents left over going to
/// the last so the shares add back exactly; and a budget compares with actual as the amount left and the share used.
/// Pure and unit-tested.
/// </summary>
public static class SchemeCostRules
{
    /// <summary>Splits an amount equally.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="count">How many shares (at least one).</param>
    /// <returns>The shares, summing to the amount.</returns>
    public static IReadOnlyList<decimal> Spread(decimal amount, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        decimal share = Math.Round(amount / count, 2, MidpointRounding.ToZero);
        decimal[] shares = [.. Enumerable.Repeat(share, count)];
        shares[^1] = amount - (share * (count - 1));
        return shares;
    }

    /// <summary>Compares a budget with actual.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="actual">The actual.</param>
    /// <returns>The comparison.</returns>
    public static BudgetComparison Compare(decimal budget, decimal actual)
        => new(budget - actual, budget == 0m ? null : Math.Round(actual * 100m / budget, 1, MidpointRounding.AwayFromZero));
}
