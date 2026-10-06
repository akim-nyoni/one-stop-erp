namespace One4All.Application.Budgeting;

/// <summary>How an annual figure is spread over the year's periods (brief §4.18.2).</summary>
public enum SpreadProfile
{
    /// <summary>Equal amounts.</summary>
    Even = 0,

    /// <summary>By the prior year's actuals in each period.</summary>
    Seasonal = 1,

    /// <summary>By weights given per period.</summary>
    Custom = 2,

    /// <summary>A capital-project S-curve: slow, then fast, then slow.</summary>
    SCurve = 3,

    /// <summary>By the working days (Monday to Friday) in each period.</summary>
    WorkingDays = 4,
}

/// <summary>Spreads an annual amount over periods. Pure.</summary>
public static class BudgetSpread
{
    /// <summary>
    /// The amount per period, to the cent, the last period taking the rounding so the parts add to the annual figure. Weights that are all
    /// zero (or none, for the even profile) spread evenly.
    /// </summary>
    /// <param name="annual">The annual amount.</param>
    /// <param name="weights">Each period's weight.</param>
    /// <returns>Each period's amount.</returns>
    public static IReadOnlyList<decimal> ByWeights(decimal annual, IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0)
        {
            return [];
        }

        decimal total = weights.Sum(w => Math.Max(0m, w));
        decimal[] parts = new decimal[weights.Count];
        decimal given = 0m;
        for (int i = 0; i < weights.Count - 1; i++)
        {
            parts[i] = Math.Round(total == 0m ? annual / weights.Count : annual * Math.Max(0m, weights[i]) / total, 2, MidpointRounding.AwayFromZero);
            given += parts[i];
        }

        parts[^1] = annual - given;
        return parts;
    }

    /// <summary>S-curve weights for n periods: the differences of a logistic curve across the year.</summary>
    /// <param name="periods">How many periods.</param>
    /// <returns>The weights.</returns>
    public static IReadOnlyList<decimal> SCurve(int periods)
    {
        static double F(double x) => 1d / (1d + Math.Exp(-10d * (x - 0.5d)));
        return [.. Enumerable.Range(1, periods).Select(i => (decimal)(F((double)i / periods) - F((double)(i - 1) / periods)))];
    }

    /// <summary>The working days (Monday to Friday) in each date range.</summary>
    /// <param name="periods">Each period's first and last day.</param>
    /// <returns>The weights.</returns>
    public static IReadOnlyList<decimal> WorkingDays(IReadOnlyList<(DateOnly Start, DateOnly End)> periods)
        => [.. periods.Select(p => (decimal)Enumerable.Range(0, p.End.DayNumber - p.Start.DayNumber + 1)
            .Count(d => p.Start.AddDays(d).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)))];
}
