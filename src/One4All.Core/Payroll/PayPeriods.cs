namespace One4All.Core.Payroll;

/// <summary>An employee's marital status.</summary>
public enum MaritalStatus
{
    /// <summary>Not recorded.</summary>
    NotRecorded = 0,

    /// <summary>Single.</summary>
    Single = 1,

    /// <summary>Married.</summary>
    Married = 2,

    /// <summary>Divorced.</summary>
    Divorced = 3,

    /// <summary>Widowed.</summary>
    Widowed = 4,
}

/// <summary>How often an employee is paid (brief §4.15: monthly salaries, weekly wages).</summary>
public enum PayFrequency
{
    /// <summary>Once a calendar month.</summary>
    Monthly = 0,

    /// <summary>Every two weeks.</summary>
    Fortnightly = 1,

    /// <summary>Every week.</summary>
    Weekly = 2,
}

/// <summary>
/// The pay periods of each frequency (brief §4.15.4: "pro-rated for part-period", tax tables "per frequency"): the window a pay date pays for, how
/// many periods a year has, the period's number in the tax year, and the share of a period someone employed for part of it earns.
/// </summary>
public static class PayPeriods
{
    /// <summary>The periods in a year.</summary>
    /// <param name="frequency">The frequency.</param>
    /// <returns>12, 26 or 52.</returns>
    public static int PerYear(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => 52,
        PayFrequency.Fortnightly => 26,
        _ => 12,
    };

    /// <summary>The days a pay date pays for: the calendar month it falls in, or the week / fortnight ending on it.</summary>
    /// <param name="frequency">The frequency.</param>
    /// <param name="payDate">The pay date.</param>
    /// <returns>The first and last day.</returns>
    public static (DateOnly From, DateOnly To) Window(PayFrequency frequency, DateOnly payDate) => frequency switch
    {
        PayFrequency.Weekly => (payDate.AddDays(-6), payDate),
        PayFrequency.Fortnightly => (payDate.AddDays(-13), payDate),
        _ => (new DateOnly(payDate.Year, payDate.Month, 1), new DateOnly(payDate.Year, payDate.Month, DateTime.DaysInMonth(payDate.Year, payDate.Month))),
    };

    /// <summary>The period's number in the (calendar) tax year: the month, or the week / fortnight counted from 1 January.</summary>
    /// <param name="frequency">The frequency.</param>
    /// <param name="payDate">The pay date.</param>
    /// <returns>The period number, from 1.</returns>
    public static int Number(PayFrequency frequency, DateOnly payDate) => Number(frequency, payDate, 1);

    /// <summary>The pay period's number within a tax year that starts in the given month (1 = January; brief §4.15.3 "tax-year straddling a fiscal year").</summary>
    /// <param name="frequency">The frequency.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="taxYearStartMonth">The tax year's first month.</param>
    /// <returns>The period number from 1.</returns>
    public static int Number(PayFrequency frequency, DateOnly payDate, int taxYearStartMonth)
    {
        int days = payDate.DayNumber - TaxYearStart(payDate, taxYearStartMonth).DayNumber;
        return frequency switch
        {
            PayFrequency.Weekly => Math.Min(52, (days / 7) + 1),
            PayFrequency.Fortnightly => Math.Min(26, (days / 14) + 1),
            _ => (((payDate.Month - Month(taxYearStartMonth)) % 12) + 12) % 12 + 1,
        };
    }

    /// <summary>The first day of the tax year a date falls in.</summary>
    /// <param name="date">The date.</param>
    /// <param name="taxYearStartMonth">The tax year's first month (1–12).</param>
    /// <returns>The first day.</returns>
    public static DateOnly TaxYearStart(DateOnly date, int taxYearStartMonth)
    {
        int month = Month(taxYearStartMonth);
        return new DateOnly(date.Month >= month ? date.Year : date.Year - 1, month, 1);
    }

    /// <summary>The tax year a date falls in, named by the calendar year it starts in (March 2026 – February 2027 is 2026; a January year is its own year).</summary>
    /// <param name="date">The date.</param>
    /// <param name="taxYearStartMonth">The tax year's first month (1–12).</param>
    /// <returns>The tax year.</returns>
    public static int TaxYear(DateOnly date, int taxYearStartMonth) => TaxYearStart(date, taxYearStartMonth).Year;

    private static int Month(int taxYearStartMonth) => taxYearStartMonth is >= 1 and <= 12 ? taxYearStartMonth : 1;

    /// <summary>The days of the period someone was employed (from engagement, to termination), and the days in the period.</summary>
    /// <param name="frequency">The frequency.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="engaged">When they started.</param>
    /// <param name="terminated">When they left, if they have.</param>
    /// <returns>Days employed in the period and days in the period.</returns>
    public static (int Worked, int InPeriod) Days(PayFrequency frequency, DateOnly payDate, DateOnly engaged, DateOnly? terminated)
    {
        (DateOnly from, DateOnly to) = Window(frequency, payDate);
        DateOnly first = engaged > from ? engaged : from;
        DateOnly last = terminated is DateOnly left && left < to ? left : to;
        return (Math.Max(0, last.DayNumber - first.DayNumber + 1), to.DayNumber - from.DayNumber + 1);
    }
}
