using One4All.Core.Common;
using One4All.Core.Workflow;

namespace One4All.Application.Abstractions.Workflow;

/// <summary>The result of starting a workflow.</summary>
/// <param name="InstanceId">The created instance id.</param>
/// <param name="Status">The initial status (Approved immediately if no stages apply).</param>
public readonly record struct WorkflowStartResult(long InstanceId, WorkflowStatus Status);

/// <summary>An in-progress workflow waiting on an approver.</summary>
/// <param name="InstanceId">The workflow instance.</param>
/// <param name="DocumentType">The document type.</param>
/// <param name="DocumentReference">The document reference.</param>
/// <param name="Value">The document value.</param>
/// <param name="InitiatedByUserId">Who submitted it.</param>
/// <param name="StageSequence">The current stage sequence.</param>
/// <param name="StageName">The current stage name.</param>
/// <param name="ApprovalsSoFar">Approvals already recorded at the current stage.</param>
/// <param name="Quorum">Approvals the stage needs.</param>
public sealed record PendingApproval(long InstanceId, string DocumentType, string DocumentReference, decimal Value, long InitiatedByUserId, int StageSequence, string StageName, int ApprovalsSoFar, int Quorum);

/// <summary>
/// The generic workflow/approval service (brief §7). One engine serves requisitions, POs, journals,
/// payment runs, budgets, master-data changes and more: start a workflow for a document, then route
/// approvals through its stages with quorum and value-band rules.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Lists the in-progress workflows waiting on a user: those whose current stage the user is eligible to
    /// act on (the stage names them, or names nobody) and has not already approved — the approver's inbox.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending approvals, oldest first.</returns>
    Task<IReadOnlyList<PendingApproval>> PendingForUserAsync(long companyId, long userId, CancellationToken ct = default);

    /// <summary>Starts a workflow for a document using the active definition for its document type.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="documentReference">The document reference/number.</param>
    /// <param name="value">The document value (for value-band routing).</param>
    /// <param name="initiatedByUserId">Who started it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The start result, or an error (e.g. no active definition).</returns>
    Task<Result<WorkflowStartResult>> StartAsync(long companyId, string documentType, string documentReference, decimal value, long initiatedByUserId, CancellationToken ct = default);

    /// <summary>Records an approve/reject/return action on a workflow instance.</summary>
    /// <param name="instanceId">The instance.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="action">The action.</param>
    /// <param name="comment">An optional comment (required for reject/return).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated status, or an error.</returns>
    Task<Result<WorkflowStatus>> ActAsync(long instanceId, long userId, WorkflowActionType action, string? comment, CancellationToken ct = default);

    /// <summary>Gets the current status of a workflow instance.</summary>
    /// <param name="instanceId">The instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status, or an error.</returns>
    Task<Result<WorkflowStatus>> GetStatusAsync(long instanceId, CancellationToken ct = default);
}
