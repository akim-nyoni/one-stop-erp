using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A request to run a supplier payment run (brief §4.6).</summary>
public sealed class PaymentRunRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The payment (accounting) date.</summary>
    public DateOnly PaymentDate { get; init; }

    /// <summary>Pay invoices due on or before this date.</summary>
    public DateOnly DueOnOrBefore { get; init; }

    /// <summary>The bank/cash account code to pay from.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>Optional restriction to specific suppliers; empty = all eligible.</summary>
    public IReadOnlyList<long> SupplierIds { get; init; } = [];
}

/// <summary>One supplier's line in a payment run.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="SupplierName">The supplier name.</param>
/// <param name="Currency">The payment currency (the invoices' currency).</param>
/// <param name="Amount">The amount paid (0 when the supplier's credits covered everything due).</param>
/// <param name="PaymentNumber">The allocated payment number (empty when nothing was paid).</param>
/// <param name="InvoicesPaid">How many invoices were settled/part-paid.</param>
/// <param name="CreditsApplied">Unused credit notes / payments on account set against the invoices before paying.</param>
public readonly record struct PaymentRunLine(long SupplierId, string SupplierCode, string SupplierName, string Currency, decimal Amount, string PaymentNumber, int InvoicesPaid, decimal CreditsApplied = 0m);

/// <summary>The result of a payment run.</summary>
/// <param name="Lines">The per-supplier payment lines.</param>
/// <param name="Total">The total paid, in base currency.</param>
/// <param name="EftFile">A generated bank EFT file (template format).</param>
public sealed record PaymentRunResult(IReadOnlyList<PaymentRunLine> Lines, decimal Total, byte[] EftFile);

/// <summary>
/// Runs a supplier payment run (brief §4.6): selects unpaid, due AP invoices, first sets each supplier's unused
/// credit notes and payments on account against them, then posts one payment per supplier and currency for the
/// remainder through the posting engine, allocates it (realising FX), and produces a bank EFT file. The whole run
/// is one database transaction. A production run adds a proposed/approval step; this posts directly.
/// </summary>
public interface IPaymentRunService
{
    /// <summary>Executes the payment run.</summary>
    /// <param name="request">The run request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result, or an error.</returns>
    Task<Result<PaymentRunResult>> RunAsync(PaymentRunRequest request, CancellationToken ct = default);
}
