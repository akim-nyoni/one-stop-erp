using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>A line change in an amendment.</summary>
/// <param name="PurchaseOrderLineId">The line.</param>
/// <param name="Quantity">The new ordered quantity.</param>
/// <param name="UnitPrice">The new unit price.</param>
public sealed record AmendedLine(long PurchaseOrderLineId, decimal Quantity, decimal UnitPrice);

/// <summary>An amendment to a purchase order.</summary>
/// <param name="AmendedOn">When.</param>
/// <param name="UserId">Who amends it.</param>
/// <param name="Reason">Why; required.</param>
/// <param name="Lines">The line changes.</param>
/// <param name="ReapprovalThresholdPercent">An increase in the order's value beyond this share needs re-approval.</param>
public sealed record AmendmentRequest(DateOnly AmendedOn, long UserId, string Reason, IReadOnlyList<AmendedLine> Lines, decimal ReapprovalThresholdPercent = 10m);

/// <summary>The outcome of an amendment.</summary>
/// <param name="Version">The order's new version.</param>
/// <param name="PreviousTotal">The total before.</param>
/// <param name="NewTotal">The total after.</param>
/// <param name="AwaitingReapproval">Whether the order now waits for re-approval.</param>
public sealed record AmendmentResult(int Version, decimal PreviousTotal, decimal NewTotal, bool AwaitingReapproval);

/// <summary>One retained version of an order.</summary>
/// <param name="Version">The version.</param>
/// <param name="TotalNet">Its total.</param>
/// <param name="Lines">Its lines.</param>
/// <param name="Reason">Why it was replaced.</param>
/// <param name="AmendedByUserId">Who replaced it.</param>
/// <param name="AmendedOn">When.</param>
public sealed record OrderVersion(int Version, decimal TotalNet, IReadOnlyList<AmendedLine> Lines, string Reason, long AmendedByUserId, DateOnly AmendedOn);

/// <summary>
/// Purchase order amendments with version history and re-approval (brief §4.3): every amendment retains the version it
/// replaces with the reason; one that raises the order's value beyond the threshold holds the order — nothing can be
/// received against it — until a different user re-approves it.
/// </summary>
public interface IPurchaseOrderAmendmentService
{
    /// <summary>Amends an order, retaining the version it replaces.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="request">The amendment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new version and whether it needs re-approval, or why it was refused.</returns>
    Task<Result<AmendmentResult>> AmendAsync(long companyId, long purchaseOrderId, AmendmentRequest request, CancellationToken ct = default);

    /// <summary>Re-approves an amended order; never by the person who amended it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="userId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ReapproveAsync(long companyId, long purchaseOrderId, long userId, CancellationToken ct = default);

    /// <summary>The retained earlier versions of an order, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history.</returns>
    Task<IReadOnlyList<OrderVersion>> HistoryAsync(long companyId, long purchaseOrderId, CancellationToken ct = default);
}
