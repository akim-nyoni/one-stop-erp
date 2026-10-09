namespace One4All.Application.Payroll;

/// <summary>
/// The liabilities a payroll builds up between paydays (brief §4.15): the annual bonus (13th cheque) earned month by month in the bonus year,
/// and gratuity earned for each year of service. Each is the amount due if it were paid on the as-at date; the provision is trued up to the
/// total. Pure.
/// </summary>
public static class PayrollProvisions
{
    /// <summary>The bonus earned in the bonus year to the as-at date: the months' pay per year, pro rata to the days served in the year.</summary>
    /// <param name="monthlyBasic">The monthly basic salary.</param>
    /// <param name="engagedOn">When the employee joined.</param>
    /// <param name="yearStart">The first day of the bonus year.</param>
    /// <param name="asAt">The as-at date.</param>
    /// <param name="monthsOfPay">The months' pay the bonus is worth for a full year (1 = a 13th cheque).</param>
    /// <returns>The bonus earned.</returns>
    public static decimal BonusEarned(decimal monthlyBasic, DateOnly engagedOn, DateOnly yearStart, DateOnly asAt, decimal monthsOfPay)
    {
        DateOnly from = engagedOn > yearStart ? engagedOn : yearStart;
        DateOnly yearEnd = yearStart.AddYears(1).AddDays(-1);
        if (asAt < from || monthlyBasic <= 0m)
        {
            return 0m;
        }

        DateOnly to = asAt < yearEnd ? asAt : yearEnd;
        decimal share = (decimal)(to.DayNumber - from.DayNumber + 1) / (yearEnd.DayNumber - yearStart.DayNumber + 1);
        return decimal.Round(monthlyBasic * monthsOfPay * share, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The gratuity earned to the as-at date: so many days' pay for each year of service (part years pro rata).</summary>
    /// <param name="monthlyBasic">The monthly basic salary.</param>
    /// <param name="engagedOn">When the employee joined.</param>
    /// <param name="asAt">The as-at date.</param>
    /// <param name="daysPerYear">The days' pay earned per year of service.</param>
    /// <param name="workingDaysPerMonth">The working days a month's pay covers (the daily rate is the monthly basic over these).</param>
    /// <returns>The gratuity earned.</returns>
    public static decimal GratuityEarned(decimal monthlyBasic, DateOnly engagedOn, DateOnly asAt, decimal daysPerYear, decimal workingDaysPerMonth)
    {
        if (asAt < engagedOn || monthlyBasic <= 0m || workingDaysPerMonth <= 0m)
        {
            return 0m;
        }

        decimal years = (decimal)(asAt.DayNumber - engagedOn.DayNumber + 1) / 365m;
        return decimal.Round(monthlyBasic / workingDaysPerMonth * daysPerYear * years, 2, MidpointRounding.AwayFromZero);
    }
}
