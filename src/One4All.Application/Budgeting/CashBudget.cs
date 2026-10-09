namespace One4All.Application.Budgeting;

/// <summary>When a budget line turns into cash: the percentage collected or paid in the month itself, the month after, and so on.</summary>
/// <param name="AccountPrefix">The accounts it applies to (the longest matching prefix wins).</param>
/// <param name="Percents">The share in month 0, 1, 2… after the budgeted month; they add up to 100.</param>
public sealed record CashTiming(string AccountPrefix, IReadOnlyList<decimal> Percents);

/// <summary>A month of the cash budget.</summary>
/// <param name="Period">The period (1–12).</param>
/// <param name="Opening">Cash at the start of the month.</param>
/// <param name="Receipts">Cash coming in.</param>
/// <param name="Payments">Cash going out.</param>
public sealed record CashBudgetRow(int Period, decimal Opening, decimal Receipts, decimal Payments)
{
    /// <summary>Receipts less payments.</summary>
    public decimal Net => Receipts - Payments;

    /// <summary>Cash at the end of the month.</summary>
    public decimal Closing => Opening + Net;
}

/// <summary>
/// The cash budget (brief §4.6): each budget line (debit-positive: expenses and assets bought positive, income negative) becomes cash as its
/// timing says — sales collected over the following months, purchases paid on credit terms — and the months run from the opening cash.
/// Cash falling after month 12 is left out of the year (reported as the carry-out). Pure.
/// </summary>
public static class CashBudget
{
    /// <summary>What is wrong with a timing, or null.</summary>
    /// <param name="timing">The timing.</param>
    /// <returns>The problem.</returns>
    public static string? Problem(CashTiming timing)
        => timing.Percents.Count == 0 || timing.Percents.Any(p => p < 0m) || timing.Percents.Sum() != 100m
            ? $"The timing for {timing.AccountPrefix} needs non-negative percentages adding up to 100."
            : null;

    /// <summary>Projects the year's cash month by month.</summary>
    /// <param name="lines">The budget lines: account, period and debit-positive amount.</param>
    /// <param name="timings">The timings (lines with none turn into cash in their own month).</param>
    /// <param name="opening">The cash at the start of the year.</param>
    /// <param name="carryOut">The net cash falling after month 12.</param>
    /// <returns>Twelve months.</returns>
    public static IReadOnlyList<CashBudgetRow> Project(IEnumerable<(string Account, int Period, decimal Amount)> lines, IReadOnlyList<CashTiming> timings, decimal opening, out decimal carryOut)
    {
        decimal[] receipts = new decimal[13], payments = new decimal[13];
        carryOut = 0m;
        foreach ((string account, int period, decimal amount) in lines)
        {
            CashTiming? timing = timings.Where(t => account.StartsWith(t.AccountPrefix, StringComparison.Ordinal)).OrderByDescending(t => t.AccountPrefix.Length).FirstOrDefault();
            IReadOnlyList<decimal> percents = timing?.Percents ?? [100m];
            decimal remaining = amount;
            for (int lag = 0; lag < percents.Count; lag++)
            {
                decimal share = lag == percents.Count - 1 ? remaining : decimal.Round(amount * percents[lag] / 100m, 2, MidpointRounding.AwayFromZero);
                remaining -= share;
                int month = period + lag;
                if (month > 12)
                {
                    carryOut -= share;
                }
                else if (share < 0m)
                {
                    receipts[month] -= share;
                }
                else
                {
                    payments[month] += share;
                }
            }
        }

        List<CashBudgetRow> rows = [];
        decimal cash = opening;
        for (int month = 1; month <= 12; month++)
        {
            CashBudgetRow row = new(month, cash, receipts[month], payments[month]);
            rows.Add(row);
            cash = row.Closing;
        }

        return rows;
    }
}
