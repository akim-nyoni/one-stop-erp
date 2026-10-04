namespace One4All.Application.Housing;

/// <summary>
/// Late-delivery penalties on a developer agreement (brief §4.10.8): each council stand the developer must deliver attracts
/// the penalty for every month or part month between the deadline and its hand-over — or, while still outstanding, the
/// assessment date. Pure and unit-tested.
/// </summary>
public static class DeveloperDelivery
{
    /// <summary>Months or part months from the deadline to a date; zero when on time.</summary>
    /// <param name="deadline">The deadline.</param>
    /// <param name="date">The delivery (or assessment) date.</param>
    /// <returns>The months late.</returns>
    public static int MonthsLate(DateOnly deadline, DateOnly date)
    {
        if (date <= deadline)
        {
            return 0;
        }

        int months = ((date.Year - deadline.Year) * 12) + date.Month - deadline.Month;
        DateOnly anniversary = deadline.AddMonths(months);
        if (anniversary > date)
        {
            months--;
            anniversary = deadline.AddMonths(months);
        }

        return anniversary < date ? months + 1 : months;
    }

    /// <summary>The penalty accrued on the council's stands as at a date.</summary>
    /// <param name="councilQuota">The stands owed to the council.</param>
    /// <param name="handedOver">The dates council stands were handed over (on or before the assessment date).</param>
    /// <param name="deadline">The delivery deadline.</param>
    /// <param name="asAt">The assessment date.</param>
    /// <param name="ratePerStandPerMonth">The penalty per stand per month late.</param>
    /// <returns>The penalty accrued.</returns>
    public static decimal Penalty(int councilQuota, IReadOnlyList<DateOnly> handedOver, DateOnly deadline, DateOnly asAt, decimal ratePerStandPerMonth)
    {
        ArgumentNullException.ThrowIfNull(handedOver);
        List<DateOnly> delivered = [.. handedOver.Where(d => d <= asAt).Order().Take(councilQuota)];
        int months = delivered.Sum(d => MonthsLate(deadline, d)) + ((councilQuota - delivered.Count) * MonthsLate(deadline, asAt));
        return Math.Round(months * ratePerStandPerMonth, 2, MidpointRounding.AwayFromZero);
    }
}
