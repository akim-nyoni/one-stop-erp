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
public sealed record TraderStatement(string TraderCode, string TraderName, string Currency, IReadOnlyList<StatementLineRow> Lines, decimal Balance);

/// <summary>Aged-balance and statement reports for the trader ledgers (brief §4.5/§4.6).</summary>
public interface ITradingReports
{
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
}
