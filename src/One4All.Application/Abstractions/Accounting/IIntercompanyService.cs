using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>
/// A request to post an intercompany transaction: one company funds or charges another, and each side
/// records its half against a mirrored intercompany account so the group nets to zero on consolidation.
/// </summary>
public sealed record IntercompanyJournalRequest
{
    /// <summary>The company giving value (paying, lending or recharging).</summary>
    public long FromCompanyId { get; init; }

    /// <summary>The company receiving value.</summary>
    public long ToCompanyId { get; init; }

    /// <summary>The amount in the shared base currency (positive).</summary>
    public decimal Amount { get; init; }

    /// <summary>The posting date (both journals).</summary>
    public DateOnly Date { get; init; }

    /// <summary>The document number (both journals carry it).</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>A narrative.</summary>
    public string? Narrative { get; init; }

    /// <summary>In the from-company: the account giving value (credited), e.g. bank or an expense recharged out.</summary>
    public string FromAccountCode { get; init; } = string.Empty;

    /// <summary>In the from-company: the intercompany receivable "due from" account (debited).</summary>
    public string FromIntercompanyAccountCode { get; init; } = string.Empty;

    /// <summary>In the to-company: the account receiving value (debited), e.g. bank or an expense.</summary>
    public string ToAccountCode { get; init; } = string.Empty;

    /// <summary>In the to-company: the intercompany payable "due to" account (credited).</summary>
    public string ToIntercompanyAccountCode { get; init; } = string.Empty;
}

/// <summary>The two journals an intercompany posting produced.</summary>
/// <param name="FromJournalNumber">The from-company journal.</param>
/// <param name="ToJournalNumber">The to-company journal.</param>
public readonly record struct IntercompanyPostingResult(string FromJournalNumber, string ToJournalNumber);

/// <summary>
/// Intercompany transactions (brief §2 multi-company): posts the mirrored pair of journals in two companies
/// atomically through the central posting engine, so the due-from and due-to balances always agree.
/// </summary>
public interface IIntercompanyService
{
    /// <summary>
    /// Posts the pair: in the from-company Dr due-from / Cr source account, in the to-company Dr target
    /// account / Cr due-to. Refuses the same company on both sides, a non-positive amount, missing account
    /// codes, or companies with different base currencies. Either both journals post or neither does.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The two journal numbers.</returns>
    Task<Result<IntercompanyPostingResult>> PostAsync(IntercompanyJournalRequest request, CancellationToken ct = default);
}
