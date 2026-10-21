using System.Globalization;

namespace One4All.Core.Payroll;

/// <summary>
/// The per-currency breakdown of a payslip line (<see cref="PayslipLine.CurrencySplit"/>): the base amounts of the line that belong to each
/// foreign currency the employee's taxable earnings were paid in, written invariant as "ZWG=30.00;EUR=12.50". The base currency has no entry:
/// its portion is the line's amount less the foreign portions. Portions are keyed by currency code, "" standing for the base currency.
/// </summary>
public static class PayslipCurrencySplit
{
    /// <summary>The longest text a line keeps.</summary>
    public const int MaxLength = 200;

    /// <summary>Writes the foreign portions (the base "" entry and nil portions are left out), or null when there are none.</summary>
    /// <param name="portions">The portions by currency code.</param>
    /// <returns>The text, or null.</returns>
    public static string? Format(IEnumerable<KeyValuePair<string, decimal>> portions)
    {
        ArgumentNullException.ThrowIfNull(portions);
        List<string> entries = [.. portions.Where(p => p.Key.Length > 0 && p.Value != 0m).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value.ToString("0.00", CultureInfo.InvariantCulture)}")];
        string text = string.Join(';', entries);
        return text.Length == 0 ? null : text[..Math.Min(text.Length, MaxLength)];
    }

    /// <summary>Reads the foreign portions written by <see cref="Format"/>; blank text, and any entry that does not read, give nothing.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The base amount of each foreign currency.</returns>
    public static IReadOnlyDictionary<string, decimal> Parse(string? text)
    {
        Dictionary<string, decimal> portions = new(StringComparer.Ordinal);
        foreach (string entry in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && decimal.TryParse(entry[(equals + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
            {
                string currency = entry[..equals].Trim().ToUpperInvariant();
                portions[currency] = portions.GetValueOrDefault(currency) + amount;
            }
        }

        return portions;
    }

    /// <summary>
    /// The amount shared in proportion to <paramref name="weights"/>: each foreign portion rounded to the cent, the base ("") taking whatever
    /// is left so the portions add up to the amount. Nil weights leave the whole amount in base.
    /// </summary>
    /// <param name="amount">The amount to share.</param>
    /// <param name="weights">The weight of each currency ("" for base).</param>
    /// <returns>The portions by currency code, the base included.</returns>
    public static Dictionary<string, decimal> Apportion(decimal amount, IReadOnlyDictionary<string, decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        decimal total = weights.Values.Sum();
        if (total == 0m)
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal) { [string.Empty] = amount };
        }

        return Exact(amount, weights.Where(w => w.Key.Length > 0).ToDictionary(w => w.Key, w => amount * w.Value / total, StringComparer.Ordinal));
    }

    /// <summary>The foreign portions as given, each rounded to the cent, with the base ("") taking the rest of the amount.</summary>
    /// <param name="amount">The line's amount.</param>
    /// <param name="portions">The portions by currency code (a base entry is ignored).</param>
    /// <returns>The portions by currency code, the base included.</returns>
    public static Dictionary<string, decimal> Exact(decimal amount, IReadOnlyDictionary<string, decimal> portions)
    {
        ArgumentNullException.ThrowIfNull(portions);
        Dictionary<string, decimal> result = new(StringComparer.Ordinal);
        foreach ((string currency, decimal portion) in portions.Where(p => p.Key.Length > 0))
        {
            result[currency] = decimal.Round(portion, 2, MidpointRounding.AwayFromZero);
        }

        result[string.Empty] = amount - result.Values.Sum();
        return result;
    }
}
