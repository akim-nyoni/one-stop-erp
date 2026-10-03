using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>Goods asked for, or issued, on a store requisition.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity.</param>
public sealed record StoreRequisitionLineRequest(long ItemId, decimal Quantity);

/// <summary>A new store requisition.</summary>
/// <param name="Date">When it is raised.</param>
/// <param name="WarehouseId">The store asked.</param>
/// <param name="ChargeAccountCode">The department's expense account.</param>
/// <param name="CostCentre">The department's cost centre, if any.</param>
/// <param name="RequestedByUserId">Who asks.</param>
/// <param name="Lines">The goods.</param>
public sealed record StoreRequisitionRequest(DateOnly Date, long WarehouseId, string ChargeAccountCode, string? CostCentre, long RequestedByUserId, IReadOnlyList<StoreRequisitionLineRequest> Lines);

/// <summary>What an issue against a requisition did.</summary>
/// <param name="IssueNoteNumber">The issue note.</param>
/// <param name="Cost">The cost charged to the department.</param>
/// <param name="FullyIssued">Whether the requisition is now fully issued.</param>
public sealed record StoreIssueResult(string IssueNoteNumber, decimal Cost, bool FullyIssued);

/// <summary>
/// Internal store requisition and issue vouchers (brief §4.8.4): a department requisitions goods, a different user
/// approves, and the store issues them — in part or in full, never more than asked — charging the department's expense
/// account and cost centre at stock cost, each issue under its own issue note number.
/// </summary>
public interface IStoreRequisitionService
{
    /// <summary>Raises a requisition, numbered from the SR sequence.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The requisition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requisition id and number, or why it was refused.</returns>
    Task<Result<(long Id, string Number)>> RaiseAsync(long companyId, StoreRequisitionRequest request, CancellationToken ct = default);

    /// <summary>Approves a requisition; never by the requester.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, long requisitionId, long approverUserId, CancellationToken ct = default);

    /// <summary>Issues goods against an approved requisition, in one unit with its issue note.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="date">The issue date.</param>
    /// <param name="lines">What is issued; each no more than still outstanding.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The issue note and cost, or why it was refused.</returns>
    Task<Result<StoreIssueResult>> IssueAsync(long companyId, long requisitionId, DateOnly date, IReadOnlyList<StoreRequisitionLineRequest> lines, CancellationToken ct = default);
}
