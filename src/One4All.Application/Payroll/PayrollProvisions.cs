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

    /// <summary>
    /// The present value of a benefit paid at retirement (IAS 19 projected unit credit, simplified): the amount earned to date at today's pay,
    /// grown with salaries to the retirement date and discounted back at the discount rate - so a provision for long-dated gratuities is not
    /// overstated. With no date of birth, or nothing to discount, the amount is left as it is.
    /// </summary>
    /// <param name="earned">The benefit earned to date at today's pay.</param>
    /// <param name="dateOfBirth">The employee's date of birth.</param>
    /// <param name="asAt">The as-at date.</param>
    /// <param name="retirementAge">The normal retirement age.</param>
    /// <param name="discountRatePercent">The annual discount rate.</param>
    /// <param name="salaryGrowthPercent">The annual salary growth assumed.</param>
    /// <returns>The present value.</returns>
    public static decimal PresentValue(decimal earned, DateOnly? dateOfBirth, DateOnly asAt, int retirementAge, decimal discountRatePercent, decimal salaryGrowthPercent)
    {
        if (earned <= 0m || dateOfBirth is not DateOnly born || discountRatePercent <= 0m)
        {
            return earned;
        }

        double years = Math.Max(0d, (born.AddYears(retirementAge).DayNumber - asAt.DayNumber) / 365.25d);
        double factor = Math.Pow((1d + (double)salaryGrowthPercent / 100d) / (1d + (double)discountRatePercent / 100d), years);
        return decimal.Round(earned * (decimal)factor, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A long-service award earned to date: the award at the employee's next service milestone (months of pay), accrued in proportion to the years
    /// served towards it since the last milestone (paid awards are not provided again).
    /// </summary>
    /// <param name="monthlyBasic">The monthly basic salary.</param>
    /// <param name="engagedOn">When the employee joined.</param>
    /// <param name="asAt">The as-at date.</param>
    /// <param name="awards">Each milestone in years of service and the months' pay awarded at it.</param>
    /// <returns>The award earned towards the next milestone (nothing beyond the last).</returns>
    public static decimal LongServiceEarned(decimal monthlyBasic, DateOnly engagedOn, DateOnly asAt, IReadOnlyList<(int Years, decimal MonthsOfPay)> awards)
    {
        ArgumentNullException.ThrowIfNull(awards);
        if (asAt < engagedOn || monthlyBasic <= 0m)
        {
            return 0m;
        }

        decimal served = (decimal)(asAt.DayNumber - engagedOn.DayNumber) / 365.25m;
        int previous = 0;
        foreach ((int milestone, decimal months) in awards.Where(a => a.Years > 0).OrderBy(a => a.Years))
        {
            if (served < milestone)
            {
                decimal share = (served - previous) / (milestone - previous);
                return decimal.Round(monthlyBasic * months * share, 2, MidpointRounding.AwayFromZero);
            }

            previous = milestone;
        }

        return 0m;
    }
}
