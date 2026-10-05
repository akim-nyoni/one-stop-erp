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

    /// <summary>Output yield: actual output as a share of planned.</summary>
    /// <param name="planned">The planned output.</param>
    /// <param name="actual">The actual output.</param>
    /// <returns>The yield percentage, or null when nothing was planned.</returns>
    public static decimal? Yield(decimal planned, decimal actual) => planned == 0m ? null : Math.Round(actual * 100m / planned, 1, MidpointRounding.AwayFromZero);
}
