namespace One4All.Application.Budgeting;

/// <summary>A virement approval band: virements up to the amount need an approver with this authorisation level.</summary>
/// <param name="UpToAmount">The largest amount in the band (null: no limit).</param>
/// <param name="Level">The authorisation level the approver needs (1–5).</param>
public sealed record VirementBandRule(decimal? UpToAmount, int Level);

/// <summary>
/// Virement approval bands (brief §4.6: "virements within delegated limits"): the band an amount falls in sets the authorisation level
/// its approver needs on Virement Approval; a virement between two departments' budgets needs one level more. Pure.
/// </summary>
public static class VirementBanding
{
    /// <summary>The highest authorisation level.</summary>
    public const int MaxLevel = 5;

    /// <summary>The level an approver needs, or null when the company has no bands.</summary>
    /// <param name="bands">The bands.</param>
    /// <param name="amount">The virement amount.</param>
    /// <param name="betweenBudgets">True when budget moves between two budgets (votes of different departments).</param>
    /// <returns>The level, 1–5.</returns>
    public static int? RequiredLevel(IReadOnlyList<VirementBandRule> bands, decimal amount, bool betweenBudgets)
    {
        if (bands.Count == 0)
        {
            return null;
        }

        VirementBandRule? band = bands.Where(b => b.UpToAmount is null || amount <= b.UpToAmount).OrderBy(b => b.UpToAmount ?? decimal.MaxValue).FirstOrDefault();
        int level = (band?.Level ?? MaxLevel) + (betweenBudgets ? 1 : 0);
        return Math.Clamp(level, 1, MaxLevel);
    }

    /// <summary>What is wrong with a set of bands, or null.</summary>
    /// <param name="bands">The bands.</param>
    /// <returns>The problem.</returns>
    public static string? Problem(IReadOnlyList<VirementBandRule> bands)
    {
        if (bands.Any(b => b.Level is < 1 or > MaxLevel))
        {
            return $"Each band needs an authorisation level from 1 to {MaxLevel}.";
        }

        if (bands.Any(b => b.UpToAmount is <= 0m))
        {
            return "Band limits must be positive.";
        }

        if (bands.Count(b => b.UpToAmount is null) > 1 || bands.Where(b => b.UpToAmount is not null).GroupBy(b => b.UpToAmount).Any(g => g.Count() > 1))
        {
            return "Each band needs its own limit, with at most one band without a limit.";
        }

        List<VirementBandRule> ordered = [.. bands.OrderBy(b => b.UpToAmount ?? decimal.MaxValue)];
        return ordered.Zip(ordered.Skip(1)).Any(p => p.Second.Level < p.First.Level) ? "A larger band cannot need a lower authorisation level." : null;
    }
}
