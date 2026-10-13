using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>One line of a requisition to create.</summary>
/// <param name="ItemId">The stock item.</param>
/// <param name="Description">The line description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The estimated unit price.</param>
/// <param name="ExpenseAccountCode">The expense account it is budgeted against, if any.</param>
public readonly record struct PurchaseRequisitionLineRequest(long ItemId, string Description, decimal Quantity, decimal UnitPrice, string? ExpenseAccountCode = null);

/// <summary>What a requisition is for, beyond its lines: when it is needed, why, and the analysis its budget is checked against.</summary>
/// <param name="RequiredBy">When the goods or services are needed.</param>
/// <param name="Justification">Why they are needed.</param>
/// <param name="Department">The department (its budget is checked).</param>
/// <param name="CostCentre">The cost centre.</param>
/// <param name="Project">The project.</param>
public sealed record RequisitionDetails(DateOnly? RequiredBy = null, string? Justification = null, string? Department = null, string? CostCentre = null, string? Project = null);

/// <summary>
/// Manages purchase requisitions (internal buy requests): capture, submit, approve/reject, and conversion into
/// a purchase order. Requisitions touch no ledger; an approved one converts into a PO via
/// <see cref="IProcurementService"/>, which then drives receiving and invoice matching.
/// </summary>
public interface IPurchaseRequisitionService
{
    /// <summary>Creates a draft requisition with its lines.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The intended supplier.</param>
    /// <param name="requisitionNumber">The requisition number.</param>
    /// <param name="requestedDate">The date requested.</param>
    /// <param name="currency">The currency.</param>
    /// <param name="lines">The requisition lines (at least one, each with a positive quantity).</param>
    /// <param name="details">When it is needed, why, and its department, cost centre and project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created requisition id.</returns>
    Task<Result<long>> CreateAsync(long companyId, long supplierId, string requisitionNumber, DateOnly requestedDate, CurrencyCode currency, IReadOnlyList<PurchaseRequisitionLineRequest> lines,
        RequisitionDetails? details = null, CancellationToken ct = default);

    /// <summary>Submits a draft requisition for approval.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> SubmitAsync(long companyId, long requisitionId, CancellationToken ct = default);

    /// <summary>Submits a requisition for approval by the user raising it: when the company defines a <c>PurchaseRequisition</c> workflow, it starts and governs the approval.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="submittedByUserId">Who submits it (the workflow's requester).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SubmitAsync(long companyId, long requisitionId, long submittedByUserId, CancellationToken ct = default);

    /// <summary>Approves a submitted requisition.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="approvedByUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> ApproveAsync(long companyId, long requisitionId, long approvedByUserId, CancellationToken ct = default);

    /// <summary>Rejects a draft or submitted requisition.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> RejectAsync(long companyId, long requisitionId, CancellationToken ct = default);

    /// <summary>
    /// Converts an approved requisition into a purchase order: creates the PO (with the requisition's lines)
    /// through the procurement service and marks the requisition converted. Refuses a requisition that is not
    /// approved.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requisitionId">The requisition.</param>
    /// <param name="orderNumber">The PO number for the new order.</param>
    /// <param name="orderDate">The order date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created purchase order id.</returns>
    Task<Result<long>> ConvertToPurchaseOrderAsync(long companyId, long requisitionId, string orderNumber, DateOnly orderDate, CancellationToken ct = default);
}
