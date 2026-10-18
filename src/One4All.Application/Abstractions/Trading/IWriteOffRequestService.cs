using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A write-off waiting for approval.</summary>
/// <param name="RequestId">The request.</param>
/// <param name="CustomerCode">The customer.</param>
/// <param name="InvoiceNumber">The invoice.</param>
/// <param name="Amount">What was open when asked.</param>
/// <param name="Reason">Why.</param>
/// <param name="RequestedByUserId">Who asked; they cannot approve it.</param>
public sealed record PendingWriteOff(long RequestId, string CustomerCode, string InvoiceNumber, decimal Amount, string Reason, long RequestedByUserId);

/// <summary>
/// Bad-debt write-offs under maker-checker (brief §4.5, §7): one user asks, another approves - in the inbox when the company has a write-off workflow - and only then is
/// the debt written off, with the VAT relief when it is overdue long enough.
/// </summary>
public interface IWriteOffRequestService
{
    /// <summary>Asks for an invoice to be written off.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The invoice.</param>
    /// <param name="badDebtAccountCode">The bad debt expense account.</param>
    /// <param name="reason">Why.</param>
    /// <param name="minimumDaysOverdue">How overdue it must be for the VAT relief.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id, or why not.</returns>
    Task<Result<long>> RequestAsync(long companyId, long openItemId, string badDebtAccountCode, string reason, int minimumDaysOverdue, long requestedByUserId, CancellationToken ct = default);

    /// <summary>Approves a request (written off at once) or rejects it with the reason; not while a workflow governs it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="approve">True to approve.</param>
    /// <param name="userId">Who decides; not who asked.</param>
    /// <param name="date">The write-off date.</param>
    /// <param name="reason">Why it is rejected (needed to reject).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write-off when approved, null when rejected, or why not.</returns>
    Task<Result<VatReliefWriteOff?>> DecideAsync(long companyId, long requestId, bool approve, long userId, DateOnly date, string? reason, CancellationToken ct = default);

    /// <summary>The requests waiting for a decision.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending requests.</returns>
    Task<IReadOnlyList<PendingWriteOff>> PendingAsync(long companyId, CancellationToken ct = default);
}
