namespace One4All.Application.Budgeting;

/// <summary>When a budget variance is material enough to need an explanation (brief §4.18.6).</summary>
/// <param name="Value">A variance of at least this much either way is material.</param>
/// <param name="Percent">A variance of at least this percentage of the budget either way is material.</param>
public sealed record VarianceThreshold(decimal Value, decimal Percent)
{
    /// <summary>A common default: 1,000 or 10%.</summary>
    public static VarianceThreshold Default { get; } = new(1_000m, 10m);
}

/// <summary>Decides which variances need commentary. Pure and unit-tested.</summary>
public static class MaterialVariance
{
    /// <summary>Whether a variance is material: at or beyond the value or the percentage of budget, either way.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="actual">The actual.</param>
    /// <param name="threshold">The threshold.</param>
    /// <returns>True when material. Spending with no budget at all is always material.</returns>
    public static bool IsMaterial(decimal budget, decimal actual, VarianceThreshold threshold)
    {
        ArgumentNullException.ThrowIfNull(threshold);
        decimal variance = Math.Abs(budget - actual);
        if (variance == 0m)
        {
            return false;
        }

        if (budget == 0m || variance >= threshold.Value)
        {
            return true;
        }

        return variance / Math.Abs(budget) * 100m >= threshold.Percent;
    }
}
