using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The outcome of a successful post.</summary>
/// <param name="JournalId">The posted journal's id.</param>
/// <param name="JournalNumber">The allocated gapless journal number.</param>
/// <param name="PostingDate">The accounting date.</param>
/// <param name="TotalBase">The total base-currency value of either side.</param>
/// <param name="Deferred">True when the journal was queued as deferred rather than posted live.</param>
public readonly record struct PostingReceipt(long JournalId, string JournalNumber, DateOnly PostingDate, decimal TotalBase, bool Deferred);

/// <summary>
/// The single, central, non-bypassable posting engine (brief §3.2). Every posting in the system -
/// manual journals, POS sales, invoices, payroll, billing runs - goes through here, inside one database
/// transaction that enforces: Dr = Cr in base currency and per transaction currency; an open period for
/// the posting module; valid, active, permitted accounts; control-account manual-journal rules; gapless
/// numbering; the audit trail; and the period-balance update. There is no other way to write to the ledger.
/// </summary>
/// <summary>A deferred journal that could not be released.</summary>
/// <param name="JournalId">The journal.</param>
/// <param name="JournalNumber">Its number.</param>
/// <param name="Reason">Why it stayed deferred.</param>
public sealed record DeferredReleaseFailure(long JournalId, string JournalNumber, string Reason);

/// <summary>The outcome of a batch release of deferred journals.</summary>
/// <param name="Released">The journal numbers released, oldest first.</param>
/// <param name="Failed">The journals that could not be released.</param>
public sealed record DeferredReleaseResult(IReadOnlyList<string> Released, IReadOnlyList<DeferredReleaseFailure> Failed);

public interface IPostingEngine
{
    /// <summary>Posts (or defers) one balanced journal.</summary>
    /// <param name="request">The journal request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A receipt on success, or an <see cref="Error"/> describing the rejection.</returns>
    Task<Result<PostingReceipt>> PostAsync(JournalRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reverses a posted journal by posting its exact inverse (every debit becomes a credit and vice versa) on
    /// the given date, linking the two and marking the original reversed. Used for accruals, prepayments and
    /// error corrections. Refuses a journal that is already reversed or is itself a reversal.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="journalId">The journal to reverse.</param>
    /// <param name="date">The reversal posting date (an open period for the journal's module).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reversal journal's receipt, or an error.</returns>
    /// <summary>
    /// Posts several journals as one all-or-nothing batch (a billing run's bills): the company, periods and accounts are read once, the journal
    /// numbers are allocated under one lock, period balances are updated once per account and one save writes the lot. Refused with
    /// <c>Posting.BatchUnsupported</c> when a journal needs the single path's own checks (deferred, submitted, manual, adjustment period, keyed,
    /// budget override, fund control) - the caller then posts one by one.
    /// </summary>
    /// <param name="requests">The journals, all of one company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A receipt per journal, in order.</returns>
    Task<Result<IReadOnlyList<PostingReceipt>>> PostBatchAsync(IReadOnlyList<JournalRequest> requests, CancellationToken ct = default)
        => Task.FromResult(Result<IReadOnlyList<PostingReceipt>>.Failure(Error.Rule("Posting.BatchUnsupported", "This posting engine posts one journal at a time.")));

    Task<Result<PostingReceipt>> ReverseAsync(long companyId, long journalId, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>
    /// Releases one deferred journal to the ledger: re-checks that its period is still open for its module,
    /// marks it posted (keeping its already-allocated number) and updates the period balances. Refuses a
    /// journal that is not deferred.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="journalId">The deferred journal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting receipt, or an error.</returns>
    Task<Result<PostingReceipt>> ReleaseDeferredAsync(long companyId, long journalId, CancellationToken ct = default);

    /// <summary>
    /// Releases every deferred journal dated on or before a date, oldest first, continuing past any that
    /// cannot be released (e.g. a closed period) and reporting them.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="upToDate">Journals with a posting date on or before this are released.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was released and what was not.</returns>
    Task<DeferredReleaseResult> ReleaseAllDeferredAsync(long companyId, DateOnly upToDate, CancellationToken ct = default);
}
