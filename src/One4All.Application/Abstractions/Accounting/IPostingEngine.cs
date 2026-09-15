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
/// The single, central, non-bypassable posting engine (brief §3.2). Every posting in the system —
/// manual journals, POS sales, invoices, payroll, billing runs — goes through here, inside one database
/// transaction that enforces: Dr = Cr in base currency and per transaction currency; an open period for
/// the posting module; valid, active, permitted accounts; control-account manual-journal rules; gapless
/// numbering; the audit trail; and the period-balance update. There is no other way to write to the ledger.
/// </summary>
public interface IPostingEngine
{
    /// <summary>Posts (or defers) one balanced journal.</summary>
    /// <param name="request">The journal request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A receipt on success, or an <see cref="Error"/> describing the rejection.</returns>
    Task<Result<PostingReceipt>> PostAsync(JournalRequest request, CancellationToken ct = default);
}
