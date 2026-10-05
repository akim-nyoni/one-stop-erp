namespace One4All.Application.Payroll;

/// <summary>A tax band: taxable income from, to (open when null), at a marginal rate.</summary>
/// <param name="From">Where the band starts.</param>
/// <param name="To">Where it ends, or null for the top band.</param>
/// <param name="RatePercent">The marginal rate.</param>
public sealed record PayeBand(decimal From, decimal? To, decimal RatePercent);

/// <summary>
/// Progressive PAYE from a tax table (brief §14 PR27, PR36): each band taxes the slice of income that falls within it at
/// its rate; the total is rounded to the cent. Pure and unit-tested.
/// </summary>
public static class PayeTable
{
    /// <summary>The tax on a month's taxable income.</summary>
    /// <param name="bands">The bands (any order).</param>
    /// <param name="taxable">The taxable income.</param>
    /// <returns>The tax.</returns>
    public static decimal Tax(IEnumerable<PayeBand> bands, decimal taxable)
    {
        decimal tax = 0m;
        foreach (PayeBand band in bands.OrderBy(b => b.From))
        {
            if (taxable <= band.From)
            {
                break;
            }

            decimal top = band.To is decimal to ? Math.Min(taxable, to) : taxable;
            tax += (top - band.From) * band.RatePercent / 100m;
        }

        return Math.Round(tax, 2, MidpointRounding.AwayFromZero);
    }
}
