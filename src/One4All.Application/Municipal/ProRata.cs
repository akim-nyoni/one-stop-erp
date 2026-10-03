namespace One4All.Application.Municipal;

/// <summary>
/// Pro-rata billing for services connected or ended part-way through a month (brief §4.9 billing run): the monthly
/// charge is billed for the days the service was in place. Pure and unit-tested.
/// </summary>
public static class ProRata
{
    /// <summary>The first and last day of the month containing a date.</summary>
    /// <param name="date">Any day in the month.</param>
    /// <returns>The month's first and last day.</returns>
    public static (DateOnly First, DateOnly Last) Month(DateOnly date)
    {
        DateOnly first = new(date.Year, date.Month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>The share of the month a service was in place: days in place over days in the month.</summary>
    /// <param name="date">Any day in the billing month.</param>
    /// <param name="start">The day the service started.</param>
    /// <param name="end">The last day of the service, if it has ended.</param>
    /// <returns>From 0 (not in place that month) to 1 (the whole month).</returns>
    public static decimal Fraction(DateOnly date, DateOnly start, DateOnly? end)
    {
        (DateOnly first, DateOnly last) = Month(date);
        DateOnly from = start > first ? start : first;
        DateOnly to = end is DateOnly e && e < last ? e : last;
        int days = to.DayNumber - from.DayNumber + 1;
        return days <= 0 ? 0m : (decimal)days / (last.Day);
    }

    /// <summary>A monthly charge for part of the month, to the cent.</summary>
    /// <param name="monthlyCharge">The full month's charge.</param>
    /// <param name="fraction">The share of the month.</param>
    /// <returns>The charge.</returns>
    public static decimal Apply(decimal monthlyCharge, decimal fraction)
        => fraction >= 1m ? monthlyCharge : decimal.Round(monthlyCharge * fraction, 2, MidpointRounding.AwayFromZero);
}
