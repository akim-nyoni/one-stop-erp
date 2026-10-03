namespace One4All.Application.Budgeting;

/// <summary>
/// Budget utilisation for budget-holder alerts (brief §4.18.5): how much of a line is used (expenditure plus commitments
/// over the appropriation) and the highest alert threshold that crosses. Pure and unit-tested.
/// </summary>
public static class BudgetUtilisation
{
    /// <summary>The usual alert thresholds: 75%, 90% and 100%.</summary>
    public static IReadOnlyList<decimal> DefaultThresholds { get; } = [75m, 90m, 100m];

    /// <summary>The percentage of the appropriation used, to two places.</summary>
    /// <param name="appropriation">The appropriation.</param>
    /// <param name="used">Expenditure plus commitments.</param>
    /// <returns>The percentage; 100 or more when spending has no appropriation.</returns>
    public static decimal PercentUsed(decimal appropriation, decimal used)
    {
        if (appropriation <= 0m)
        {
            return used > 0m ? 100m : 0m;
        }

        return decimal.Round(used / appropriation * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The highest threshold the percentage has reached, if any.</summary>
    /// <param name="percentUsed">The percentage used.</param>
    /// <param name="thresholds">The alert thresholds.</param>
    /// <returns>The threshold crossed, or null when none is.</returns>
    public static decimal? HighestCrossed(decimal percentUsed, IReadOnlyList<decimal> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        decimal[] crossed = [.. thresholds.Where(t => percentUsed >= t)];
        return crossed.Length == 0 ? null : crossed.Max();
    }
}
