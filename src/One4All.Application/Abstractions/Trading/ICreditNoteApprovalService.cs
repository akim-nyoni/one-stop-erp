using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A submitted credit note: posted at once, or held in its approval workflow.</summary>
/// <param name="Posted">Whether it posted at once (no credit-note workflow).</param>
/// <param name="JournalNumber">The journal when posted.</param>
public readonly record struct CreditNoteSubmission(bool Posted, string? JournalNumber)
{
    /// <summary>Gets the credit note's number (the one given, or the one taken from the "SCN" sequence when it was left blank).</summary>
    public string? DocumentNumber { get; init; }
}

/// <summary>
/// Customer credit notes under approval (brief §7): when the company has a credit-note workflow a submitted credit note is held
/// (<see cref="Core.Trading.PendingCreditNote"/>) and only posts once approved; a rejection leaves the ledger untouched. Without a workflow it
/// posts at once.
/// </summary>
public interface ICreditNoteApprovalService
{
    /// <summary>Submits a credit note.</summary>
    Task<Result<CreditNoteSubmission>> SubmitAsync(SalesCreditNoteRequest request, long submittedByUserId, CancellationToken ct = default);
}
