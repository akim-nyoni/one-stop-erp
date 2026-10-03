namespace One4All.Application.Trading;

/// <summary>An overdue invoice's uncharged interest period.</summary>
/// <param name="DueDate">When it fell due.</param>
/// <param name="ChargeFrom">The first day not yet charged (the later of the due date and the last run).</param>
/// <param name="Outstanding">Its outstanding amount.</param>
public readonly record struct ChargeableItem(DateOnly DueDate, DateOnly ChargeFrom, decimal Outstanding);

/// <summary>A customer's unallocated payment or credit note.</summary>
/// <param name="Date">Its date — from when it stops interest on what it covers.</param>
/// <param name="Amount">Its unallocated amount.</param>
public readonly record struct UnallocatedCredit(DateOnly Date, decimal Amount);

/// <summary>The interest due on a customer's overdue invoices once their unallocated credits are taken into account.</summary>
/// <param name="Interest">The interest to charge.</param>
/// <param name="OverdueAfterCredits">The overdue amount the credits do not cover.</param>
public readonly record struct CreditAdjustedInterest(decimal Interest, decimal OverdueAfterCredits);

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

    /// <summary>
    /// Interest on a customer's overdue invoices after their unallocated payments and credit notes: credits are set
    /// against the invoices oldest credit to oldest due date, as allocation will. The part of an invoice a credit
    /// covers accrues only from its charge-from date to the credit's date (nothing when the credit came first); the
    /// part no credit covers accrues to the run date.
    /// </summary>
    /// <param name="items">The overdue invoices.</param>
    /// <param name="credits">The customer's unallocated credits.</param>
    /// <param name="asAtDate">The run date.</param>
    /// <param name="annualRatePercent">The annual rate as a percent.</param>
    /// <returns>The interest and the overdue amount left uncovered.</returns>
    public static CreditAdjustedInterest InterestAfterCredits(IEnumerable<ChargeableItem> items, IEnumerable<UnallocatedCredit> credits, DateOnly asAtDate, decimal annualRatePercent)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(credits);

        List<UnallocatedCredit> ordered = [.. credits.Where(c => c.Amount > 0m).OrderBy(c => c.Date)];
        int next = 0;
        decimal creditLeft = ordered.Count > 0 ? ordered[0].Amount : 0m;
        decimal interest = 0m;
        decimal uncovered = 0m;
        foreach (ChargeableItem item in items.Where(i => i.Outstanding > 0m).OrderBy(i => i.DueDate))
        {
            decimal left = item.Outstanding;
            while (left > 0m && next < ordered.Count)
            {
                decimal covered = Math.Min(left, creditLeft);
                int days = Math.Min(ordered[next].Date.DayNumber, asAtDate.DayNumber) - item.ChargeFrom.DayNumber;
                interest += Interest(covered, days, annualRatePercent);
                left -= covered;
                creditLeft -= covered;
                if (creditLeft == 0m && ++next < ordered.Count)
                {
                    creditLeft = ordered[next].Amount;
                }
            }

            if (left > 0m)
            {
                interest += Interest(left, asAtDate.DayNumber - item.ChargeFrom.DayNumber, annualRatePercent);
                uncovered += left;
            }
        }

        return new CreditAdjustedInterest(interest, uncovered);
    }
}
