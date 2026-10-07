namespace One4All.Application.Banking;

/// <summary>Where a forecast cash flow comes from.</summary>
public enum CashFlowSource
{
    /// <summary>Customer invoices falling due (overdue ones in the first week).</summary>
    Receivables = 1,

    /// <summary>Supplier invoices falling due, other than those in a saved payment run.</summary>
    Payables = 2,

    /// <summary>Saved payment runs awaiting approval, on their payment date.</summary>
    PaymentRuns = 3,

    /// <summary>Open sales orders not yet invoiced, on delivery plus the customer's terms.</summary>
    SalesOrders = 4,

    /// <summary>Open purchase orders not yet invoiced, on the required date plus the supplier's terms.</summary>
    PurchaseOrders = 5,

    /// <summary>Standing orders paid from the bank.</summary>
    StandingOrders = 6,

    /// <summary>Payroll: net pay on the pay day, deductions remitted ten days later (from the last regular run).</summary>
    Payroll = 7,
}

/// <summary>One forecast cash flow (positive in, negative out).</summary>
/// <param name="Date">When.</param>
/// <param name="Source">Where it comes from.</param>
/// <param name="Reference">The document or party.</param>
/// <param name="Amount">The amount (base currency).</param>
public readonly record struct CashFlowLine(DateOnly Date, CashFlowSource Source, string Reference, decimal Amount);

/// <summary>One week of the forecast.</summary>
/// <param name="WeekStart">The first day of the week.</param>
/// <param name="Opening">Cash at the start.</param>
/// <param name="Inflows">Cash in.</param>
/// <param name="Outflows">Cash out (positive).</param>
/// <param name="Closing">Cash at the end.</param>
/// <param name="BySource">Net flow per source.</param>
/// <param name="BelowMinimum">Whether the closing cash is below the minimum to hold.</param>
public sealed record CashForecastWeek(DateOnly WeekStart, decimal Opening, decimal Inflows, decimal Outflows, decimal Closing, IReadOnlyDictionary<CashFlowSource, decimal> BySource,
    bool BelowMinimum);

/// <summary>A weekly cash forecast.</summary>
/// <param name="From">The first day.</param>
/// <param name="OpeningBalance">The bank balances at the start.</param>
/// <param name="Minimum">The minimum cash to hold.</param>
/// <param name="Weeks">The weeks.</param>
/// <param name="Lines">Every flow forecast, by date.</param>
public sealed record CashForecast(DateOnly From, decimal OpeningBalance, decimal Minimum, IReadOnlyList<CashForecastWeek> Weeks, IReadOnlyList<CashFlowLine> Lines)
{
    /// <summary>The first week cash falls below the minimum, if any.</summary>
    public DateOnly? FirstShortfall => Weeks.FirstOrDefault(w => w.BelowMinimum)?.WeekStart;

    /// <summary>The lowest closing balance over the horizon.</summary>
    public decimal LowestClosing => Weeks.Count == 0 ? OpeningBalance : Weeks.Min(w => w.Closing);
}

/// <summary>Buckets forecast cash flows into weeks from an opening balance (brief §4.4 cash-flow forecast). Pure and unit-tested.</summary>
public static class CashForecastBuilder
{
    /// <summary>Builds the forecast: flows before the start fall into the first week (overdue), flows after the horizon are left out.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="weeks">How many weeks.</param>
    /// <param name="openingBalance">Cash at the start.</param>
    /// <param name="minimum">The minimum cash to hold.</param>
    /// <param name="lines">The flows.</param>
    /// <returns>The forecast.</returns>
    public static CashForecast Build(DateOnly from, int weeks, decimal openingBalance, decimal minimum, IEnumerable<CashFlowLine> lines)
    {
        DateOnly end = from.AddDays(weeks * 7);
        List<CashFlowLine> inRange = [.. lines.Where(l => l.Date < end && l.Amount != 0m).Select(l => l.Date < from ? l with { Date = from } : l).OrderBy(l => l.Date).ThenBy(l => l.Source)];
        List<CashForecastWeek> rows = [];
        decimal balance = openingBalance;
        for (int w = 0; w < weeks; w++)
        {
            DateOnly start = from.AddDays(w * 7), next = start.AddDays(7);
            List<CashFlowLine> week = [.. inRange.Where(l => l.Date >= start && l.Date < next)];
            decimal inflows = week.Where(l => l.Amount > 0m).Sum(l => l.Amount), outflows = -week.Where(l => l.Amount < 0m).Sum(l => l.Amount);
            decimal closing = balance + inflows - outflows;
            rows.Add(new CashForecastWeek(start, balance, inflows, outflows, closing, week.GroupBy(l => l.Source).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)), closing < minimum));
            balance = closing;
        }

        return new CashForecast(from, openingBalance, minimum, rows, inRange);
    }
}
