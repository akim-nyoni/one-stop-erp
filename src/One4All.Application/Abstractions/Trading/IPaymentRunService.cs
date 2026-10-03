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

    /// <summary>
    /// A withholding tax code applied to payments to suppliers without a valid tax clearance (see
    /// <see cref="PaymentRequest.WithholdingTaxCode"/>); the bank file then carries the net amount.
    /// </summary>
    public string? WithholdingTaxCode { get; init; }
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
/// <param name="Withheld">Withholding tax deducted from the payment (payment currency); the bank pays <c>Amount − Withheld</c>.</param>
public readonly record struct PaymentRunLine(long SupplierId, string SupplierCode, string SupplierName, string Currency, decimal Amount, string PaymentNumber, int InvoicesPaid, decimal CreditsApplied = 0m, decimal Withheld = 0m)
{
    /// <summary>What the bank actually pays.</summary>
    public decimal NetPaid => Amount - Withheld;
}

/// <summary>The result of a payment run.</summary>
/// <param name="Lines">The per-supplier payment lines.</param>
/// <param name="Total">The total paid, in base currency.</param>
/// <param name="EftFile">A generated bank EFT file (template format).</param>
public sealed record PaymentRunResult(IReadOnlyList<PaymentRunLine> Lines, decimal Total, byte[] EftFile);

/// <summary>One supplier's line in a payment run proposal (nothing posted).</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="SupplierName">The supplier name.</param>
/// <param name="Currency">The invoices' currency.</param>
/// <param name="InvoicesDue">How many invoices are due.</param>
/// <param name="AmountDue">Their outstanding total.</param>
/// <param name="CreditsAvailable">The supplier's unused credit notes / payments on account in that currency.</param>
/// <param name="CreditsToApply">How much of those credits the run would set against the invoices.</param>
/// <param name="ToPay">What the run would pay (0 for a supplier on hold).</param>
/// <param name="OnHold">True when the supplier is on hold and would be skipped.</param>
/// <param name="Withholding">Withholding tax that would be deducted (the supplier has no valid tax clearance).</param>
public readonly record struct PaymentProposalLine(long SupplierId, string SupplierCode, string SupplierName, string Currency, int InvoicesDue, decimal AmountDue, decimal CreditsAvailable, decimal CreditsToApply, decimal ToPay, bool OnHold, decimal Withholding = 0m, bool BankChangePending = false);

/// <summary>A payment run proposal: what <see cref="IPaymentRunService.RunAsync"/> would do with the same request.</summary>
/// <param name="PaymentDate">The payment date.</param>
/// <param name="Lines">One line per supplier and currency with invoices due, including suppliers on hold.</param>
public sealed record PaymentRunProposal(DateOnly PaymentDate, IReadOnlyList<PaymentProposalLine> Lines)
{
    /// <summary>The number of payments the run would make.</summary>
    public int PaymentCount => Lines.Count(l => l.ToPay > 0m);

    /// <summary>The amounts to pay, per currency.</summary>
    public IReadOnlyDictionary<string, decimal> ToPayByCurrency => Lines
        .Where(l => l.ToPay > 0m)
        .GroupBy(l => l.Currency, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Sum(l => l.ToPay), StringComparer.Ordinal);
}

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

    /// <summary>
    /// Proposes the run without posting anything: per supplier and currency, the invoices due, the credits that would
    /// be applied and the amount that would be paid; suppliers on hold are listed with nothing to pay.
    /// </summary>
    /// <param name="request">The run request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The proposal.</returns>
    Task<Result<PaymentRunProposal>> ProposeAsync(PaymentRunRequest request, CancellationToken ct = default);
}
