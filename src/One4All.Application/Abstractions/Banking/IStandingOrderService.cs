using One4All.Core.Banking;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>A standing order to set up.</summary>
public sealed class StandingOrderRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the reference.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Gets the bank paid from.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the amount each time.</summary>
    public decimal Amount { get; init; }

    /// <summary>Gets how often.</summary>
    public RecurrenceFrequency Frequency { get; init; } = RecurrenceFrequency.Monthly;

    /// <summary>Gets the first payment date.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>Gets the last date it may pay.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Gets the supplier paid (or set the debit account instead).</summary>
    public long? SupplierId { get; init; }

    /// <summary>Gets the GL account debited when it pays no supplier.</summary>
    public string? DebitAccountCode { get; init; }

    /// <summary>Gets what it is for.</summary>
    public string Narrative { get; init; } = string.Empty;
}

/// <summary>A direct debit mandate to set up.</summary>
public sealed class DirectDebitMandateRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the mandate reference.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Gets the customer.</summary>
    public long CustomerId { get; init; }

    /// <summary>Gets the account collected from.</summary>
    public string CustomerBankAccount { get; init; } = string.Empty;

    /// <summary>Gets the bank collected into.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>Gets how often.</summary>
    public RecurrenceFrequency Frequency { get; init; } = RecurrenceFrequency.Monthly;

    /// <summary>Gets the first collection date.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>Gets the last date it may collect.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Gets a fixed amount, or null to collect what the customer owes.</summary>
    public decimal? FixedAmount { get; init; }

    /// <summary>Gets the most one collection may take.</summary>
    public decimal? MaximumAmount { get; init; }
}

/// <summary>One payment or collection made by a run.</summary>
/// <param name="Reference">The standing order or mandate.</param>
/// <param name="DueDate">The occurrence.</param>
/// <param name="DocumentNumber">The payment or receipt document.</param>
/// <param name="Amount">The amount.</param>
/// <param name="IsCollection">True for a direct debit collected, false for a standing order paid.</param>
public sealed record RecurringBankLine(string Reference, DateOnly DueDate, string DocumentNumber, decimal Amount, bool IsCollection);

/// <summary>What a run did.</summary>
/// <param name="Lines">The payments and collections.</param>
/// <param name="NothingOwed">Direct debit occurrences passed over because nothing was owed.</param>
public sealed record RecurringBankRun(IReadOnlyList<RecurringBankLine> Lines, int NothingOwed);

/// <summary>Standing orders and direct debit collections (brief §4.5 cash book, §4.6 receivables).</summary>
public interface IStandingOrderService
{
    /// <summary>Sets up a standing order: to a supplier or to a GL account (exactly one), a positive amount, a unique reference.</summary>
    /// <param name="request">The standing order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its id.</returns>
    Task<Result<long>> CreateStandingOrderAsync(StandingOrderRequest request, CancellationToken ct = default);

    /// <summary>Sets up a direct debit mandate: fixed amount or what is owed, optionally capped, a unique reference.</summary>
    /// <param name="request">The mandate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its id.</returns>
    Task<Result<long>> CreateMandateAsync(DirectDebitMandateRequest request, CancellationToken ct = default);

    /// <summary>Stops a standing order or cancels a mandate by its reference.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> CancelAsync(long companyId, string reference, CancellationToken ct = default);

    /// <summary>
    /// Runs everything due up to a date: each standing order occurrence pays the supplier (purchase ledger) or debits its GL account
    /// against the bank; each direct debit occurrence receipts the fixed amount, or what the customer owes (capped), to the sales ledger.
    /// Each occurrence is its own unit of work numbered reference-yyyyMMdd and is never run twice.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asOf">Run occurrences on or before this date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was paid and collected.</returns>
    Task<Result<RecurringBankRun>> RunDueAsync(long companyId, DateOnly asOf, CancellationToken ct = default);
}
