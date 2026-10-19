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

    /// <summary>Gets the bank file layout the EFT file is written in (the generic pipe-delimited file when null).</summary>
    public string? EftLayoutCode { get; init; }

    /// <summary>Gets how the run pays: an EFT file (the default), a cheque to each supplier, or cash.</summary>
    public Core.Trading.PaymentRunMethod Method { get; init; }

    /// <summary>Gets the first cheque number of a cheque run; each supplier's cheque follows on.</summary>
    public string? FirstChequeNumber { get; init; }

    /// <summary>Only invoices in this currency (all when blank).</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// The funds available for the run, in its currency (null: no limit). A proposal then pays suppliers by priority (1 first), then
    /// supplier code, until the funds run out; the rest is deferred to a later run.
    /// </summary>
    public decimal? FundsAvailable { get; init; }
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
/// <param name="DiscountTaken">Settlement discount taken (payment currency): invoices paid within the supplier's discount days, less the discount.</param>
public readonly record struct PaymentRunLine(long SupplierId, string SupplierCode, string SupplierName, string Currency, decimal Amount, string PaymentNumber, int InvoicesPaid, decimal CreditsApplied = 0m, decimal Withheld = 0m,
    decimal DiscountTaken = 0m)
{
    /// <summary>What the bank actually pays.</summary>
    public decimal NetPaid => Amount - Withheld;
}

/// <summary>The result of a payment run.</summary>
/// <param name="Lines">The per-supplier payment lines.</param>
/// <param name="Total">The total paid, in base currency.</param>
/// <param name="EftFile">A generated bank EFT file (template format).</param>
public sealed record PaymentRunResult(IReadOnlyList<PaymentRunLine> Lines, decimal Total, byte[] EftFile, IReadOnlyList<string>? ChequeNumbers = null);

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
public readonly record struct PaymentProposalLine(long SupplierId, string SupplierCode, string SupplierName, string Currency, int InvoicesDue, decimal AmountDue, decimal CreditsAvailable, decimal CreditsToApply, decimal ToPay, bool OnHold, decimal Withholding = 0m, bool BankChangePending = false)
{
    /// <summary>Gets the supplier's payment priority (1 first … 9 last).</summary>
    public int Priority { get; init; } = 5;

    /// <summary>Gets what the funds available left unpaid this time.</summary>
    public decimal Deferred { get; init; }
}

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

    /// <summary>Saves a proposed run for review: every supplier due, held ones left out with the reason.</summary>
    /// <param name="request">The selection.</param>
    /// <param name="proposedByUserId">Who proposes it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The saved run.</returns>
    Task<Result<SavedPaymentRun>> SaveProposalAsync(PaymentRunRequest request, long proposedByUserId, CancellationToken ct = default);

    /// <summary>Leaves a supplier out of a saved run, puts them back, or part-pays them (up to what is due).</summary>
    Task<Result<SavedPaymentRun>> SetLineAsync(long companyId, string runNumber, string supplierCode, string currency, bool included, decimal? amount, CancellationToken ct = default);

    /// <summary>Approves a saved run (not by its proposer) and pays exactly what it holds.</summary>
    Task<Result<PaymentRunResult>> ApproveAsync(long companyId, string runNumber, long approvedByUserId, CancellationToken ct = default);

    /// <summary>Withdraws a saved run before approval.</summary>
    Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default);

    /// <summary>A saved run.</summary>
    Task<Result<SavedPaymentRun>> GetAsync(long companyId, string runNumber, CancellationToken ct = default);
}

/// <summary>A saved payment run and its lines.</summary>
/// <param name="Number">Its number.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="PaymentDate">The payment date.</param>
/// <param name="Lines">The proposed payments.</param>
public sealed record SavedPaymentRun(string Number, Core.Trading.PaymentRunStatus Status, DateOnly PaymentDate, IReadOnlyList<SavedPaymentLine> Lines)
{
    /// <summary>What it pays, by currency.</summary>
    public IReadOnlyDictionary<string, decimal> TotalByCurrency => Lines.Where(l => l.Included).GroupBy(l => l.Currency, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount), StringComparer.Ordinal);
}

/// <summary>One saved proposed payment.</summary>
public sealed record SavedPaymentLine(string SupplierCode, string Currency, decimal Due, decimal Amount, bool Included, string? Note, string? PaymentNumber);
