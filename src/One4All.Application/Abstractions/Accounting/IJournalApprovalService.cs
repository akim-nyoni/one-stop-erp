using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>A manual journal held for a second user's approval.</summary>
/// <param name="JournalId">The journal.</param>
/// <param name="JournalNumber">Its number, allocated on submission.</param>
/// <param name="PostingDate">The accounting date.</param>
/// <param name="Narrative">The journal narrative.</param>
/// <param name="TotalBaseDebit">The journal value (total base debits).</param>
/// <param name="PreparedByUserId">Who prepared it; they cannot approve it.</param>
public sealed record JournalAwaitingApproval(long JournalId, string JournalNumber, DateOnly PostingDate, string? Narrative, decimal TotalBaseDebit, long PreparedByUserId);

/// <summary>
/// Maker-checker control over manual journals (brief §4): a preparer submits a journal, which is validated, numbered and
/// held unposted; a different user approves it, which posts it, or rejects it, which cancels it while keeping its number.
/// The database refuses a held journal posting without an approver, or one approved by its preparer.
/// </summary>
public interface IJournalApprovalService
{
    /// <summary>Submits a manual journal for approval: it is validated and numbered now, and posts only once approved.</summary>
    /// <param name="request">The journal; it is treated as manual.</param>
    /// <param name="preparedByUserId">The preparer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The receipt for the held journal, or why it is invalid.</returns>
    Task<Result<PostingReceipt>> SubmitAsync(JournalRequest request, long preparedByUserId, CancellationToken ct = default);

    /// <summary>Lists the journals awaiting approval, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The held journals.</returns>
    Task<IReadOnlyList<JournalAwaitingApproval>> AwaitingApprovalAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Approves a held journal and posts it, in one unit: if posting fails (say the period has closed) the approval is
    /// not kept.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="journalId">The journal.</param>
    /// <param name="approverUserId">The approver; never the preparer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting receipt, or why it was refused.</returns>
    Task<Result<PostingReceipt>> ApproveAsync(long companyId, long journalId, long approverUserId, CancellationToken ct = default);

    /// <summary>Rejects a held journal: it is cancelled, never posts, and keeps its number so the sequence has no gap.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="journalId">The journal.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectAsync(long companyId, long journalId, string reason, CancellationToken ct = default);
}
