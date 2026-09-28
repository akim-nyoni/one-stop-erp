using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>Working-capital efficiency metrics for a period.</summary>
/// <param name="FromDate">Start of the period.</param>
/// <param name="ToDate">End of the period (the balances are taken as at this date).</param>
/// <param name="ReceivablesBalance">The accounts-receivable control balance at the end.</param>
/// <param name="CreditSales">Revenue recognised over the period.</param>
/// <param name="DaysSalesOutstanding">DSO: receivables expressed as days of sales.</param>
/// <param name="PayablesBalance">The accounts-payable control balance at the end.</param>
/// <param name="CreditPurchases">Expenses/purchases over the period.</param>
/// <param name="DaysPayableOutstanding">DPO: payables expressed as days of purchases.</param>
public sealed record WorkingCapitalMetrics(
    DateOnly FromDate, DateOnly ToDate,
    decimal ReceivablesBalance, decimal CreditSales, decimal DaysSalesOutstanding,
    decimal PayablesBalance, decimal CreditPurchases, decimal DaysPayableOutstanding);

/// <summary>
/// Computes working-capital efficiency ratios (DSO and DPO) from the ledger: how many days of sales are
/// tied up in receivables and how many days of purchases are financed by payables.
/// </summary>
public interface IWorkingCapitalService
{
    /// <summary>Computes DSO and DPO for a company over a period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the period.</param>
    /// <param name="toDate">End of the period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The working-capital metrics.</returns>
    Task<Result<WorkingCapitalMetrics>> ComputeAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}
