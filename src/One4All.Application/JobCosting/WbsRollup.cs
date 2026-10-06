namespace One4All.Application.JobCosting;

/// <summary>One WBS element as the rollup sees it.</summary>
/// <param name="Code">The dotted code.</param>
/// <param name="Name">The name.</param>
/// <param name="BudgetCost">The budget (activities).</param>
/// <param name="CostHead">The cost head (activities).</param>
/// <param name="EstimateToComplete">The site's estimate to complete, if given.</param>
public sealed record WbsElementInput(string Code, string Name, decimal BudgetCost, string? CostHead, decimal? EstimateToComplete);

/// <summary>A line of the cost-to-complete breakdown.</summary>
/// <param name="Code">The code ("" for costs on no activity).</param>
/// <param name="Name">The name.</param>
/// <param name="Level">The depth (1 = top level).</param>
/// <param name="Budget">The budget.</param>
/// <param name="Actual">The cost to date.</param>
/// <param name="Committed">Committed but not yet incurred (open subcontract value).</param>
/// <param name="EstimateToComplete">Still to spend beyond actual and committed.</param>
/// <param name="ForecastFinalCost">Actual + committed + estimate to complete.</param>
/// <param name="Variance">Budget less forecast final cost (negative: overrun).</param>
/// <param name="PercentComplete">Actual as a percentage of forecast final cost.</param>
public sealed record WbsLine(string Code, string Name, int Level, decimal Budget, decimal Actual, decimal Committed, decimal EstimateToComplete, decimal ForecastFinalCost, decimal Variance,
    decimal PercentComplete);

/// <summary>Rolls costs up a work breakdown structure and forecasts the cost to complete (brief §4.12).</summary>
public static class WbsRollup
{
    /// <summary>The code of the line holding costs whose cost head is on no activity.</summary>
    public const string Unassigned = "";

    /// <summary>
    /// Builds the breakdown: each activity takes the actual and committed cost of its cost head; its estimate to complete is the site's
    /// estimate or, failing one, the budget left after actual and committed (never negative). Phases sum their children. Costs on cost
    /// heads no activity carries form a last, unassigned line. Lines come in code order (numeric segments compare as numbers).
    /// </summary>
    /// <param name="elements">The elements.</param>
    /// <param name="actualByHead">Actual cost per cost head.</param>
    /// <param name="committedByHead">Committed cost per cost head.</param>
    /// <returns>The lines, then the total as the last element of the tuple.</returns>
    public static (IReadOnlyList<WbsLine> Lines, WbsLine Total) Build(IReadOnlyList<WbsElementInput> elements, IReadOnlyDictionary<string, decimal> actualByHead,
        IReadOnlyDictionary<string, decimal> committedByHead)
    {
        HashSet<string> parents = [.. elements.Select(e => Parent(e.Code)).OfType<string>()];
        Dictionary<string, WbsLine> lines = new(StringComparer.Ordinal);
        foreach (WbsElementInput e in elements.Where(e => !parents.Contains(e.Code)))
        {
            decimal actual = e.CostHead is null ? 0m : actualByHead.GetValueOrDefault(e.CostHead);
            decimal committed = e.CostHead is null ? 0m : committedByHead.GetValueOrDefault(e.CostHead);
            decimal etc = e.EstimateToComplete ?? Math.Max(0m, e.BudgetCost - actual - committed);
            lines[e.Code] = Line(e.Code, e.Name, e.BudgetCost, actual, committed, etc);
        }

        // Deepest phases first, so each phase sums children that are already complete.
        foreach (WbsElementInput e in elements.Where(e => parents.Contains(e.Code)).OrderByDescending(e => Depth(e.Code)))
        {
            List<WbsLine> children = [.. lines.Values.Where(l => Parent(l.Code) == e.Code)];
            lines[e.Code] = Line(e.Code, e.Name, children.Sum(c => c.Budget), children.Sum(c => c.Actual), children.Sum(c => c.Committed), children.Sum(c => c.EstimateToComplete));
        }

        List<WbsLine> ordered = [.. lines.Values.OrderBy(l => l.Code, SegmentComparer.Instance)];
        HashSet<string> carried = [.. elements.Select(e => e.CostHead).OfType<string>()];
        decimal looseActual = actualByHead.Where(p => !carried.Contains(p.Key)).Sum(p => p.Value);
        decimal looseCommitted = committedByHead.Where(p => !carried.Contains(p.Key)).Sum(p => p.Value);
        if (looseActual != 0m || looseCommitted != 0m)
        {
            ordered.Add(Line(Unassigned, "Costs on no activity", 0m, looseActual, looseCommitted, 0m));
        }

        List<WbsLine> top = [.. ordered.Where(l => l.Code.Length == 0 || Parent(l.Code) is null)];
        WbsLine total = Line("TOTAL", "Total", top.Sum(l => l.Budget), top.Sum(l => l.Actual), top.Sum(l => l.Committed), top.Sum(l => l.EstimateToComplete)) with { Level = 0 };
        return (ordered, total);
    }

    /// <summary>The parent code of a dotted code, or null at the top.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The parent code.</returns>
    public static string? Parent(string code)
    {
        int dot = code.LastIndexOf('.');
        return dot < 0 ? null : code[..dot];
    }

    private static int Depth(string code) => code.Length == 0 ? 1 : code.Count(c => c == '.') + 1;

    private static WbsLine Line(string code, string name, decimal budget, decimal actual, decimal committed, decimal etc)
    {
        decimal final = actual + committed + etc;
        return new WbsLine(code, name, Depth(code), budget, actual, committed, etc, final, budget - final, final == 0m ? 0m : decimal.Round(actual / final * 100m, 2, MidpointRounding.ToEven));
    }

    // "1.10" after "1.9": numeric segments compare as numbers, others ordinally.
    private sealed class SegmentComparer : IComparer<string>
    {
        public static readonly SegmentComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x == y)
            {
                return 0;
            }

            if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
            {
                return string.IsNullOrEmpty(x) ? 1 : -1;
            }

            string[] a = x.Split('.'), b = y.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int c = long.TryParse(a[i], out long na) && long.TryParse(b[i], out long nb) ? na.CompareTo(nb) : string.CompareOrdinal(a[i], b[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}
