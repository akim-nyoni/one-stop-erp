namespace One4All.Application.Trading;

/// <summary>One band of a provision matrix: debts at least this many days past due are provided at this rate.</summary>
/// <param name="FromDaysPastDue">The first day past due the band covers (0 = not yet due or due today).</param>
/// <param name="RatePercent">The expected loss rate, as a percent of the exposure.</param>
/// <param name="Label">A display label (e.g. "31–60 days").</param>
public readonly record struct EclBand(int FromDaysPastDue, decimal RatePercent, string Label);

/// <summary>
/// An IFRS 9 simplified-approach provision matrix (brief §4.5): lifetime expected credit loss as a percent of trade
/// receivables by how far past due they are. Bands are matched on the highest <see cref="EclBand.FromDaysPastDue"/>
/// not above the debt's days past due.
/// </summary>
/// <param name="Bands">The bands.</param>
public sealed record EclMatrix(IReadOnlyList<EclBand> Bands)
{
    /// <summary>A typical matrix: current 1%, 1–30 days 3%, 31–60 10%, 61–90 25%, over 90 50%.</summary>
    public static EclMatrix Default => new(
    [
        new EclBand(0, 1m, "Current"),
        new EclBand(1, 3m, "1–30 days"),
        new EclBand(31, 10m, "31–60 days"),
        new EclBand(61, 25m, "61–90 days"),
        new EclBand(91, 50m, "Over 90 days"),
    ]);

    /// <summary>The band a debt falls in.</summary>
    /// <param name="daysPastDue">Days past due (negative when not yet due).</param>
    /// <returns>The band.</returns>
    public EclBand BandFor(int daysPastDue)
    {
        int days = Math.Max(0, daysPastDue);
        return Bands.Where(b => b.FromDaysPastDue <= days).OrderByDescending(b => b.FromDaysPastDue).FirstOrDefault();
    }
}

/// <summary>A receivable to provide for.</summary>
/// <param name="Outstanding">What is still owed on it (after the customer's unallocated credits).</param>
/// <param name="DaysPastDue">Days past its due date (negative when not yet due).</param>
public readonly record struct EclExposure(decimal Outstanding, int DaysPastDue);

/// <summary>One band's line of the provision.</summary>
/// <param name="Band">The band.</param>
/// <param name="Exposure">The receivables in it.</param>
/// <param name="Provision">The provision on them.</param>
public readonly record struct EclBandResult(EclBand Band, decimal Exposure, decimal Provision);

/// <summary>Computes the expected credit loss allowance from a provision matrix. Pure and unit-tested.</summary>
public static class ExpectedCreditLoss
{
    /// <summary>Groups the exposures into the matrix's bands and applies each band's rate.</summary>
    /// <param name="exposures">The receivables.</param>
    /// <param name="matrix">The matrix.</param>
    /// <returns>One line per band, in band order (bands with nothing in them included at zero).</returns>
    public static IReadOnlyList<EclBandResult> Compute(IEnumerable<EclExposure> exposures, EclMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(exposures);
        ArgumentNullException.ThrowIfNull(matrix);

        Dictionary<int, decimal> byBand = matrix.Bands.ToDictionary(b => b.FromDaysPastDue, _ => 0m);
        foreach (EclExposure exposure in exposures.Where(e => e.Outstanding > 0m))
        {
            int key = matrix.BandFor(exposure.DaysPastDue).FromDaysPastDue;
            byBand[key] += exposure.Outstanding;
        }

        return [.. matrix.Bands.OrderBy(b => b.FromDaysPastDue).Select(b => new EclBandResult(b, byBand[b.FromDaysPastDue],
            decimal.Round(byBand[b.FromDaysPastDue] * b.RatePercent / 100m, 2, MidpointRounding.AwayFromZero)))];
    }
}
