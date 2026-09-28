namespace One4All.Application.Trading;

/// <summary>
/// The interest arithmetic for finance charges on overdue customer balances: simple interest on the
/// overdue amount at an annual rate, accrued actual/365 over the days the item is overdue. Kept pure so it
/// is unit-tested; the finance-charge service supplies the overdue items and posts the resulting charge.
/// </summary>
public static class FinanceCharge
{
    /// <summary>Days per year used for the actual/365 accrual.</summary>
    public const decimal DaysPerYear = 365m;

    /// <summary>Computes the interest on an overdue amount.</summary>
    /// <param name="overdueAmount">The outstanding overdue amount (must be positive to charge).</param>
    /// <param name="daysOverdue">Days past the (grace-adjusted) due date.</param>
    /// <param name="annualRatePercent">The annual interest rate as a percent (e.g. 12 for 12% p.a.).</param>
    /// <returns>The interest, rounded to two decimals; zero when nothing is chargeable.</returns>
    public static decimal Interest(decimal overdueAmount, int daysOverdue, decimal annualRatePercent)
    {
        if (overdueAmount <= 0m || daysOverdue <= 0 || annualRatePercent <= 0m)
        {
            return 0m;
        }

        decimal interest = overdueAmount * (annualRatePercent / 100m) * daysOverdue / DaysPerYear;
        return Math.Round(interest, 2, MidpointRounding.AwayFromZero);
    }
}
