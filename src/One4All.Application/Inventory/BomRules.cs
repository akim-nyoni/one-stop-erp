namespace One4All.Application.Inventory;

/// <summary>Bill-of-materials arithmetic: standard requirements, rolled-up cost and yield.</summary>
public static class BomRules
{
    /// <summary>The standard quantity of a component for an output: per-batch quantity scaled to the output, plus expected scrap.</summary>
    /// <param name="perBatch">The component quantity per batch.</param>
    /// <param name="scrapPercent">The expected scrap percentage.</param>
    /// <param name="batchOutput">The batch's output.</param>
    /// <param name="output">The output wanted.</param>
    /// <returns>The standard quantity, to four places.</returns>
    public static decimal Requirement(decimal perBatch, decimal scrapPercent, decimal batchOutput, decimal output)
        => batchOutput <= 0m ? 0m : Math.Round(perBatch * (1m + (scrapPercent / 100m)) * output / batchOutput, 4, MidpointRounding.AwayFromZero);

    /// <summary>The rolled-up cost of one finished unit from its components' unit costs.</summary>
    /// <param name="lines">Each component's per-batch quantity, scrap percentage and unit cost.</param>
    /// <param name="batchOutput">The batch's output.</param>
    /// <returns>The cost per finished unit, to four places.</returns>
    public static decimal UnitCost(IEnumerable<(decimal Quantity, decimal ScrapPercent, decimal UnitCost)> lines, decimal batchOutput)
        => Math.Round(lines.Sum(l => Requirement(l.Quantity, l.ScrapPercent, batchOutput, 1m) * l.UnitCost), 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Splits a cost across parts in proportion to their weights (equally when every weight is zero), to the cent, the last part taking
    /// the rounding so the parts sum to the total.
    /// </summary>
    /// <param name="total">The cost to split.</param>
    /// <param name="weights">Each part's weight (e.g. quantity × unit cost).</param>
    /// <returns>Each part's share.</returns>
    public static IReadOnlyList<decimal> SplitCost(decimal total, IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0)
        {
            return [];
        }

        decimal sum = weights.Sum();
        decimal[] shares = new decimal[weights.Count];
        decimal allocated = 0m;
        for (int i = 0; i < weights.Count - 1; i++)
        {
            decimal share = sum == 0m ? total / weights.Count : total * weights[i] / sum;
            shares[i] = Math.Round(share, 2, MidpointRounding.AwayFromZero);
            allocated += shares[i];
        }

        shares[^1] = total - allocated;
        return shares;
    }

    /// <summary>Output yield: actual output as a share of planned.</summary>
    /// <param name="planned">The planned output.</param>
    /// <param name="actual">The actual output.</param>
    /// <returns>The yield percentage, or null when nothing was planned.</returns>
    public static decimal? Yield(decimal planned, decimal actual) => planned == 0m ? null : Math.Round(actual * 100m / planned, 1, MidpointRounding.AwayFromZero);
}
