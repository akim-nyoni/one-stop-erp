using One4All.Core.Common;
using One4All.Core.Workflow;

namespace One4All.Application.Abstractions.Workflow;

/// <summary>
/// What a module does when the approval workflow for one of its documents ends (brief §7): approved — the document goes ahead (a
/// requisition is approved, a journal posts); rejected or returned — it is turned down. Runs in the same unit of work as the last
/// decision, so if the document cannot go ahead the decision is not recorded either.
/// </summary>
public interface IWorkflowDocumentHandler
{
    /// <summary>The document type its workflows are defined for (e.g. <c>PurchaseRequisition</c>, <c>Journal</c>).</summary>
    string DocumentType { get; }

    /// <summary>Applies the outcome to the document.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentReference">The document's reference (its id).</param>
    /// <param name="outcome">Approved, Rejected or Returned.</param>
    /// <param name="decidedByUserId">Who made the last decision.</param>
    /// <param name="comment">Their comment.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Success, or why the document cannot follow the decision.</returns>
    Task<Result> ConcludeAsync(long companyId, string documentReference, WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default);
}

/// <summary>The document types the built-in handlers serve.</summary>
public static class WorkflowDocumentTypes
{
    /// <summary>Purchase requisitions.</summary>
    public const string PurchaseRequisition = "PurchaseRequisition";

    /// <summary>Manual journals held for approval.</summary>
    public const string Journal = "Journal";

    /// <summary>Saved supplier payment runs (reference: the run number).</summary>
    public const string PaymentRun = "PaymentRun";

    /// <summary>Proposed municipal billing runs (reference: the run number).</summary>
    public const string BillingRun = "BillingRun";

    /// <summary>Customer credit notes held for approval (reference: the credit note number).</summary>
    public const string CreditNote = "CreditNote";

    /// <summary>Debt write-off requests (reference: the debt case number).</summary>
    public const string WriteOff = "WriteOff";

    /// <summary>Purchase orders raised directly (reference: the order number).</summary>
    public const string PurchaseOrder = "PurchaseOrder";

    /// <summary>A school bursary request.</summary>
    public const string Bursary = "Bursary";

    /// <summary>An employee's leave application (reference: <c>LV-</c> and the application id).</summary>
    public const string LeaveApplication = "LeaveApplication";

    /// <summary>A stocktake's variance posting (reference: <c>ST-</c> and the stocktake id).</summary>
    public const string Stocktake = "Stocktake";

    /// <summary>A recommended budget awaiting approval (reference: <c>BUD-</c> and the budget id).</summary>
    public const string Budget = "Budget";

    /// <summary>A proposed housing allocation run (reference: the run number).</summary>
    public const string AllocationRun = "AllocationRun";
}
