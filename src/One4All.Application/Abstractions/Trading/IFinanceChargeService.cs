using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A request to raise finance charges on overdue customer balances as at a date.</summary>
public sealed class FinanceChargeRunRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The date the charges are raised (interest accrues up to this date).</summary>
    public DateOnly AsAtDate { get; init; }

    /// <summary>The annual interest rate as a percent (e.g. 12 for 12% p.a.).</summary>
    public decimal AnnualRatePercent { get; init; }

    /// <summary>Days of grace after the due date before interest starts to accrue.</summary>
    public int GraceDays { get; init; }

    /// <summary>The minimum charge per customer; a computed interest below this is skipped.</summary>
    public decimal MinimumCharge { get; init; }

    /// <summary>The interest-income account credited.</summary>
    public string InterestIncomeAccountCode { get; init; } = string.Empty;

    /// <summary>A prefix for the finance-charge document numbers.</summary>
    public string DocumentPrefix { get; init; } = "FC";
}

/// <summary>The finance charge raised for one customer.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="OverdueAmount">The total overdue balance interest was charged on.</param>
/// <param name="MaxDaysOverdue">The greatest days-overdue among the customer's items.</param>
/// <param name="Interest">The interest raised.</param>
/// <param name="DocumentNumber">The finance-charge invoice document number.</param>
public readonly record struct CustomerFinanceCharge(long CustomerId, decimal OverdueAmount, int MaxDaysOverdue, decimal Interest, string DocumentNumber);

/// <summary>The result of a finance-charge run.</summary>
/// <param name="Charges">The charges raised, one per customer charged.</param>
/// <param name="TotalInterest">The total interest raised.</param>
public sealed record FinanceChargeRunResult(IReadOnlyList<CustomerFinanceCharge> Charges, decimal TotalInterest);

/// <summary>
/// Raises interest (finance charges) on overdue customer balances (brief §4.5). Scans the AR ledger for
/// overdue open items, computes simple interest per customer, and posts a finance-charge invoice
/// (Dr AR control / Cr interest income) for each — so the charge ages and can be collected like any invoice.
/// </summary>
public interface IFinanceChargeService
{
    /// <summary>Raises finance charges on overdue customer balances as at the request date.</summary>
    /// <param name="request">The run request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The charges raised, or an error.</returns>
    Task<Result<FinanceChargeRunResult>> RaiseChargesAsync(FinanceChargeRunRequest request, CancellationToken ct = default);
}
