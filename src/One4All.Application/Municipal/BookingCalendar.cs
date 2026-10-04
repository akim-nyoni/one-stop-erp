namespace One4All.Application.Municipal;

/// <summary>
/// Facility booking arithmetic (brief §4.9 hall hire, market stalls and other council revenue streams): whether two
/// date ranges clash, how many days a booking covers, its fee and the refund on cancellation. Pure and unit-tested.
/// </summary>
public static class BookingCalendar
{
    /// <summary>Whether two inclusive date ranges share at least one day.</summary>
    /// <param name="aFrom">The first range's first day.</param>
    /// <param name="aTo">The first range's last day.</param>
    /// <param name="bFrom">The second range's first day.</param>
    /// <param name="bTo">The second range's last day.</param>
    /// <returns>True when they overlap.</returns>
    public static bool Overlaps(DateOnly aFrom, DateOnly aTo, DateOnly bFrom, DateOnly bTo) => aFrom <= bTo && bFrom <= aTo;

    /// <summary>The days an inclusive range covers.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <returns>The number of days (0 when the range is backwards).</returns>
    public static int Days(DateOnly from, DateOnly to) => Math.Max(0, to.DayNumber - from.DayNumber + 1);

    /// <summary>The fee for a booking at a daily rate.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="dailyRate">The daily rate.</param>
    /// <returns>The fee, to the cent.</returns>
    public static decimal Fee(DateOnly from, DateOnly to, decimal dailyRate) => decimal.Round(Days(from, to) * dailyRate, 2, MidpointRounding.AwayFromZero);

    /// <summary>The refund on cancelling a booking: a percentage of the fee, to the cent.</summary>
    /// <param name="fee">The booking fee.</param>
    /// <param name="refundPercent">The percentage refunded (0–100).</param>
    /// <returns>The refund.</returns>
    public static decimal Refund(decimal fee, decimal refundPercent)
        => decimal.Round(fee * Math.Clamp(refundPercent, 0m, 100m) / 100m, 2, MidpointRounding.AwayFromZero);
}
