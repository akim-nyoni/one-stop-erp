using System.Globalization;

namespace One4All.Core.Inventory;

/// <summary>
/// Monthly seasonal factors for the replenishment forecast (brief §4.6: forecast by simple moving average / seasonal factor): twelve
/// non-negative multipliers, January first. Pure and unit-tested.
/// </summary>
public static class SeasonalFactors
{
    /// <summary>Reads twelve comma-separated factors.</summary>
    /// <param name="text">The factors, or blank for none.</param>
    /// <param name="factors">The factors read (null for none).</param>
    /// <returns>Whether the text was valid (blank, or exactly twelve non-negative numbers below 100).</returns>
    public static bool TryParse(string? text, out decimal[]? factors)
    {
        factors = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 12)
        {
            return false;
        }

        decimal[] read = new decimal[12];
        for (int m = 0; m < 12; m++)
        {
            if (!decimal.TryParse(parts[m], NumberStyles.Number, CultureInfo.InvariantCulture, out read[m]) || read[m] < 0m || read[m] >= 100m)
            {
                return false;
            }
        }

        factors = read;
        return true;
    }

    /// <summary>The factor for a date's month (1 when there are none or the text is not valid).</summary>
    /// <param name="text">The stored factors.</param>
    /// <param name="date">The date.</param>
    /// <returns>The multiplier.</returns>
    public static decimal For(string? text, DateOnly date) => TryParse(text, out decimal[]? factors) && factors is not null ? factors[date.Month - 1] : 1m;

    /// <summary>Writes factors back in the stored form.</summary>
    /// <param name="factors">The twelve factors.</param>
    /// <returns>The text.</returns>
    public static string Format(IReadOnlyList<decimal> factors) => string.Join(',', factors.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)));
}
