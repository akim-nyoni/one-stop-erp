using System.Globalization;
using One4All.Core.Common;

namespace One4All.Application.Payroll;

/// <summary>A band of a pay element's lookup table.</summary>
/// <param name="From">The lowest value in the band.</param>
/// <param name="To">The value the band stops below (null: open-ended).</param>
/// <param name="Amount">The amount, or the percentage of the value when <paramref name="Percent"/>.</param>
/// <param name="Percent">Whether the amount is a percentage of the looked-up value.</param>
public sealed record PayLookupBand(decimal From, decimal? To, decimal Amount, bool Percent);

/// <summary>
/// A pay element's lookup table (brief §4.15.2 "table lookup" basis): bands of a variable (BASIC, GROSS, YEARS_SERVICE, a grade's number…) to an amount
/// or a percentage, written "0-1000:50; 1000-5000:2.5%; 5000-:150". The value falls in the band whose from it reaches and whose to it is below.
/// </summary>
public static class PayLookupTable
{
    /// <summary>Reads a table.</summary>
    /// <param name="text">The bands.</param>
    /// <returns>The bands in order, or why the table is wrong.</returns>
    public static Result<IReadOnlyList<PayLookupBand>> Parse(string? text)
    {
        List<PayLookupBand> bands = [];
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
            string[] range = bits[0].Split('-', StringSplitOptions.TrimEntries);
            bool percent = bits.Length == 2 && bits[1].EndsWith('%');
            if (bits.Length != 2 || range.Length != 2 || !decimal.TryParse(range[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal from)
                || !(range[1].Length == 0 || decimal.TryParse(range[1], NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                || !decimal.TryParse(bits[1].TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
            {
                return Error.Validation("Payroll.LookupBand", $"Write each band as from-to:amount or from-to:rate% (\"{part}\").");
            }

            decimal? to = range[1].Length == 0 ? null : decimal.Parse(range[1], CultureInfo.InvariantCulture);
            if (to <= from)
            {
                return Error.Validation("Payroll.LookupBand", $"Band {part} ends before it starts.");
            }

            bands.Add(new PayLookupBand(from, to, amount, percent));
        }

        if (bands.Count == 0)
        {
            return Error.Validation("Payroll.LookupEmpty", "A lookup table needs at least one band.");
        }

        List<PayLookupBand> ordered = [.. bands.OrderBy(b => b.From)];
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].To is not decimal end || end > ordered[i].From)
            {
                return Error.Validation("Payroll.LookupOverlap", "The lookup bands overlap.");
            }
        }

        return ordered;
    }

    /// <summary>The amount for a value (nothing when no band holds it).</summary>
    /// <param name="bands">The bands.</param>
    /// <param name="value">The looked-up value.</param>
    /// <returns>The amount.</returns>
    public static decimal Amount(IReadOnlyList<PayLookupBand> bands, decimal value)
    {
        ArgumentNullException.ThrowIfNull(bands);
        PayLookupBand? band = bands.LastOrDefault(b => value >= b.From && (b.To is null || value < b.To));
        return band is null ? 0m : band.Percent ? decimal.Round(value * band.Amount / 100m, 2, MidpointRounding.AwayFromZero) : band.Amount;
    }
}
