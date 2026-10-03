using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A supplier's bank details.</summary>
/// <param name="AccountName">The account name.</param>
/// <param name="AccountNumber">The account number.</param>
/// <param name="BankName">The bank and branch.</param>
public sealed record SupplierBankDetails(string? AccountName, string? AccountNumber, string? BankName);

/// <summary>A bank-detail change waiting for a second user.</summary>
/// <param name="ChangeId">The change.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">The supplier's code.</param>
/// <param name="Current">The details now held.</param>
/// <param name="Requested">The details asked for.</param>
/// <param name="RequestedByUserId">Who asked; they cannot approve it.</param>
public sealed record PendingBankChange(long ChangeId, long SupplierId, string SupplierCode, SupplierBankDetails Current, SupplierBankDetails Requested, long RequestedByUserId);

/// <summary>
/// Dual authorisation of supplier bank-detail changes (brief §4.6): one user requests, a different user approves, and only
/// then does the supplier's record change. A supplier with a change pending is held out of payment runs.
/// </summary>
public interface ISupplierBankDetailsService
{
    /// <summary>Requests a change of a supplier's bank details.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="details">The new details.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The change id, or why it was refused (one change at a time per supplier).</returns>
    Task<Result<long>> RequestChangeAsync(long companyId, long supplierId, SupplierBankDetails details, long requestedByUserId, CancellationToken ct = default);

    /// <summary>Lists the changes waiting for approval.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending changes, oldest first.</returns>
    Task<IReadOnlyList<PendingBankChange>> PendingAsync(long companyId, CancellationToken ct = default);

    /// <summary>Approves a change and applies it to the supplier; never by the requester.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="changeId">The change.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, long changeId, long approverUserId, CancellationToken ct = default);

    /// <summary>Rejects a change, leaving the supplier's details as they were.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="changeId">The change.</param>
    /// <param name="userId">Who rejects it.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectAsync(long companyId, long changeId, long userId, string reason, CancellationToken ct = default);
}
