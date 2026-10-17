using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>An aged-balance row for one trader (base currency), brief §14 reports 29/52.</summary>
/// <param name="TraderCode">The customer/supplier code.</param>
/// <param name="TraderName">The trader name.</param>
/// <param name="Current">Not yet due.</param>
/// <param name="Days1To30">1–30 days overdue.</param>
/// <param name="Days31To60">31–60 days.</param>
/// <param name="Days61To90">61–90 days.</param>
/// <param name="Days91Plus">91+ days.</param>
public sealed record AgeingRow(string TraderCode, string TraderName, decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Days91Plus)
{
    /// <summary>The total outstanding for the trader.</summary>
    public decimal Total => Current + Days1To30 + Days31To60 + Days61To90 + Days91Plus;
}

/// <summary>An aged-balances report.</summary>
/// <param name="Ledger">The sub-ledger.</param>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Rows">The per-trader rows with a non-zero balance.</param>
public sealed record AgeingReport(TradingLedger Ledger, DateOnly AsAtDate, IReadOnlyList<AgeingRow> Rows)
{
    /// <summary>The grand total outstanding.</summary>
    public decimal Total => Rows.Sum(r => r.Total);
}

/// <summary>One line of a customer/supplier statement.</summary>
/// <param name="Date">Document date.</param>
/// <param name="DocumentType">The item type.</param>
/// <param name="DocumentNumber">The document number.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="Amount">The signed transaction amount (debits positive, credits negative).</param>
/// <param name="Outstanding">The outstanding transaction amount.</param>
public sealed record StatementLineRow(DateOnly Date, OpenItemType DocumentType, string DocumentNumber, DateOnly DueDate, decimal Amount, decimal Outstanding);

/// <summary>An open-item statement for one trader.</summary>
/// <param name="TraderCode">The trader code.</param>
/// <param name="TraderName">The trader name.</param>
/// <param name="Currency">The trading currency.</param>
/// <param name="Lines">The statement lines.</param>
/// <param name="Balance">The outstanding balance in transaction currency.</param>
public sealed record TraderStatement(string TraderCode, string TraderName, string Currency, IReadOnlyList<StatementLineRow> Lines, decimal Balance, AgeingBasis AgeingBasis = AgeingBasis.DueDate);

/// <summary>One customer's credit-control exposure (base currency).</summary>
/// <param name="CustomerCode">The customer code.</param>
/// <param name="CustomerName">The customer name.</param>
/// <param name="CreditLimit">The approved credit limit (0 = unlimited).</param>
/// <param name="Balance">The current outstanding receivable balance.</param>
/// <param name="Available">The remaining credit (limit − balance; 0 for an unlimited customer).</param>
/// <param name="OnHold">Whether the account is on stop.</param>
/// <param name="OverLimit">Whether the balance has breached a finite limit.</param>
public sealed record CreditExposureRow(string CustomerCode, string CustomerName, decimal CreditLimit, decimal Balance, decimal Available, bool OnHold, bool OverLimit);

/// <summary>A customer credit-control exposure report as at a date.</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Rows">The per-customer rows.</param>
public sealed record CreditExposureReport(DateOnly AsAtDate, IReadOnlyList<CreditExposureRow> Rows);

/// <summary>One time-bucket of the cash-flow forecast (base currency).</summary>
/// <param name="Bucket">The bucket label (e.g. "Overdue", "Due 0-30").</param>
/// <param name="Inflow">Expected receipts from receivables falling due in the bucket.</param>
/// <param name="Outflow">Expected payments on payables falling due in the bucket.</param>
/// <param name="Net">Inflow − Outflow for the bucket.</param>
/// <param name="RunningNet">The cumulative net across this and earlier buckets.</param>
public sealed record CashFlowForecastRow(string Bucket, decimal Inflow, decimal Outflow, decimal Net, decimal RunningNet);

/// <summary>A short-term cash-flow forecast from open receivables and payables, bucketed by due date.</summary>
/// <param name="AsAtDate">The as-at date the buckets are measured from.</param>
/// <param name="Rows">The per-bucket rows.</param>
public sealed record CashFlowForecastReport(DateOnly AsAtDate, IReadOnlyList<CashFlowForecastRow> Rows);

/// <summary>One outstanding supplier invoice on the payments-due schedule (base currency).</summary>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="SupplierName">The supplier name.</param>
/// <param name="DocumentNumber">The invoice number.</param>
/// <param name="DocumentDate">The invoice date.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="Outstanding">The outstanding amount payable.</param>
/// <param name="DaysUntilDue">Days from the as-at date until due (negative when overdue).</param>
public sealed record PaymentDueRow(string SupplierCode, string SupplierName, string DocumentNumber, DateOnly DocumentDate, DateOnly DueDate, decimal Outstanding, int DaysUntilDue);

/// <summary>The payments-due schedule: outstanding payables ordered by due date.</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Rows">The invoice rows.</param>
/// <param name="Total">The total outstanding across the rows.</param>
public sealed record PaymentsDueReport(DateOnly AsAtDate, IReadOnlyList<PaymentDueRow> Rows, decimal Total);

/// <summary>Aged-balance and statement reports for the trader ledgers (brief §4.5/§4.6).</summary>
public interface ITradingReports
{
    /// <summary>
    /// Produces the payments-due schedule: every outstanding supplier invoice ordered by due date, with the
    /// days until due (negative when overdue) relative to <paramref name="asAtDate"/> — the line-level companion
    /// to the bucketed cash-flow forecast, driving the payment run.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payments-due schedule.</returns>
    Task<PaymentsDueReport> PaymentsDueAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>
    /// Produces a short-term cash-flow forecast: outstanding receivables (inflows) and payables (outflows)
    /// bucketed by when they fall due relative to <paramref name="asAtDate"/> — overdue, then 0-30 / 31-60 /
    /// 61-90 / 90+ days ahead — with the net and running net per bucket.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date the buckets are measured from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cash-flow forecast.</returns>
    Task<CashFlowForecastReport> CashFlowForecastAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>
    /// Produces the customer credit-control exposure as at a date: each customer's credit limit, current
    /// receivable balance, remaining credit and on-hold / over-limit flags. Customers with a balance or a
    /// finite limit are included.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The credit-exposure report.</returns>
    Task<CreditExposureReport> CreditExposureAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Produces the aged debtors report as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aged debtors report.</returns>
    Task<AgeingReport> AgedDebtorsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Produces the aged creditors report as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aged creditors report.</returns>
    Task<AgeingReport> AgedCreditorsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Produces an open-item statement for a customer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement, or null if the customer is unknown.</returns>
    Task<TraderStatement?> CustomerStatementAsync(long companyId, long customerId, CancellationToken ct = default);

    /// <summary>Produces an open-item statement for a supplier (AP).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement, or null if the supplier is unknown.</returns>
    Task<TraderStatement?> SupplierStatementAsync(long companyId, long supplierId, CancellationToken ct = default);

    /// <summary>Produces a remittance advice for a supplier payment: the invoices it settled.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="paymentOpenItemId">The AP payment open item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The remittance advice, or null if the item is not an AP payment.</returns>
    Task<RemittanceAdvice?> RemittanceAdviceAsync(long companyId, long paymentOpenItemId, CancellationToken ct = default);
}

/// <summary>One settled invoice on a remittance advice.</summary>
/// <param name="InvoiceNumber">The supplier invoice number.</param>
/// <param name="InvoiceDate">The invoice date.</param>
/// <param name="AllocatedAmount">The amount of the payment applied to it.</param>
public sealed record RemittanceLine(string InvoiceNumber, DateOnly InvoiceDate, decimal AllocatedAmount);

/// <summary>A remittance advice: which invoices a supplier payment settled.</summary>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="SupplierName">The supplier name.</param>
/// <param name="PaymentNumber">The payment document number.</param>
/// <param name="PaymentDate">The payment date.</param>
/// <param name="Currency">The payment currency.</param>
/// <param name="Lines">The invoices settled by the payment.</param>
/// <param name="TotalAllocated">The total allocated across the lines.</param>
public sealed record RemittanceAdvice(string SupplierCode, string SupplierName, string PaymentNumber, DateOnly PaymentDate, string Currency, IReadOnlyList<RemittanceLine> Lines, decimal TotalAllocated);
