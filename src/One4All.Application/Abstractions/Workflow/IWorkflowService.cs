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
/// <param name="OnBehalfOfUserId">When it reaches the user as someone's delegate, whose approval it is.</param>
public sealed record PendingApproval(long InstanceId, string DocumentType, string DocumentReference, decimal Value, long InitiatedByUserId, int StageSequence, string StageName, int ApprovalsSoFar, int Quorum,
    long? OnBehalfOfUserId = null, int Attachments = 0);

/// <summary>
/// The generic workflow/approval service (brief §7). One engine serves requisitions, POs, journals,
/// payment runs, budgets, master-data changes and more: start a workflow for a document, then route
/// approvals through its stages with quorum and value-band rules.
/// </summary>
/// <summary>One entry of a document's approval thread: a remark, or a decision with its comment.</summary>
/// <param name="AtUtc">When.</param>
/// <param name="UserId">Who.</param>
/// <param name="UserName">Their user name.</param>
/// <param name="Kind">"Comment", or the decision (Approve, Reject, Return).</param>
/// <param name="Text">What was written (null for a decision without a comment).</param>
public sealed record WorkflowThreadEntry(DateTimeOffset AtUtc, long UserId, string UserName, string Kind, string? Text);

/// <summary>One item of a bulk action.</summary>
/// <param name="InstanceId">The workflow instance.</param>
/// <param name="Succeeded">Whether the action was taken.</param>
/// <param name="Status">The instance's status after it.</param>
/// <param name="Error">Why not, when it failed.</param>
public sealed record BulkActionResult(long InstanceId, bool Succeeded, One4All.Core.Workflow.WorkflowStatus? Status, string? Error);

public interface IWorkflowService
{
    /// <summary>
    /// Lists the in-progress workflows waiting on a user: those whose current stage the user is eligible to
    /// act on (the stage names them, or names nobody) and has not already approved - the approver's inbox.
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

    /// <summary>Acts through a single-use link emailed to an approver: valid once, before it expires, and only while the workflow is still at that stage.</summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="action">Approve or reject.</param>
    /// <param name="comment">The comment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow's status after the action.</returns>
    Task<Result<WorkflowStatus>> ActByTokenAsync(string token, WorkflowActionType action, string? comment, CancellationToken ct = default);

    /// <summary>What an emailed link would act on, for the page that asks the approver to confirm.</summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document type, reference and value, or why the link cannot be used.</returns>
    Task<Result<(string DocumentType, string Reference, decimal Value)>> DescribeTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Delegates an approver's approvals to another user between two dates (out of office); refused for oneself or overlapping an
    /// existing delegation of the same approver (<c>Workflow.DelegationOverlap</c>). Only the approver delegates their own approvals, unless
    /// the acting user is a system administrator (<c>Workflow.DelegationNotOwn</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromUserId">The approver who is away.</param>
    /// <param name="toUserId">The delegate.</param>
    /// <param name="startsOn">The first day.</param>
    /// <param name="endsOn">The last day.</param>
    /// <param name="reason">Why.</param>
    /// <param name="actingUserId">Who records the delegation: the approver who is away, or a system administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The delegation id.</returns>
    Task<Result<long>> DelegateAsync(long companyId, long fromUserId, long toUserId, DateOnly startsOn, DateOnly endsOn, string reason, long actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Adds an approver to an in-progress instance's current stage. The one adding must be able to approve that stage (or be a system
    /// administrator); the requester (<c>Workflow.OwnDocument</c>) and the one adding (<c>Workflow.AddSelf</c>) are never added.
    /// </summary>
    /// <param name="instanceId">The instance.</param>
    /// <param name="userId">The approver to add.</param>
    /// <param name="addedByUserId">Who adds them.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> AddApproverAsync(long instanceId, long userId, long addedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Adds a remark to a document's approval thread without deciding it (brief §7): the requester, anyone who has acted on it, an approver added to it,
    /// or anyone who may approve its current stage.
    /// </summary>
    /// <param name="instanceId">The workflow instance.</param>
    /// <param name="userId">Who writes it.</param>
    /// <param name="text">The remark (up to 2,000 characters).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> CommentAsync(long instanceId, long userId, string text, CancellationToken ct = default);

    /// <summary>A document's approval thread: its remarks and every decision with its comment, in time order.</summary>
    /// <param name="instanceId">The workflow instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The thread, or why not.</returns>
    Task<Result<IReadOnlyList<WorkflowThreadEntry>>> ThreadAsync(long instanceId, CancellationToken ct = default);

    /// <summary>
    /// Escalates every in-progress instance that has waited at its stage longer than the stage allows: the stage's escalation user is
    /// added as an approver of it (once per stage visit).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many instances escalated.</returns>
    Task<int> EscalateOverdueAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Starts a workflow with the document's attributes (e.g. Department, Category), which stage conditions read and which are kept as the
    /// document's snapshot at each decision.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="documentReference">The document.</param>
    /// <param name="value">Its value.</param>
    /// <param name="initiatedByUserId">Who started it.</param>
    /// <param name="attributes">Its attributes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The instance.</returns>
    Task<Result<WorkflowStartResult>> StartAsync(long companyId, string documentType, string documentReference, decimal value, long initiatedByUserId, IReadOnlyDictionary<string, string> attributes,
        CancellationToken ct = default);

    /// <summary>Acts on many instances at once (bulk approve); each succeeds or fails on its own.</summary>
    /// <param name="instanceIds">The instances.</param>
    /// <param name="userId">Who acts.</param>
    /// <param name="action">The action.</param>
    /// <param name="comment">A comment for each.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Each instance's outcome.</returns>
    Task<IReadOnlyList<BulkActionResult>> ActManyAsync(IReadOnlyList<long> instanceIds, long userId, One4All.Core.Workflow.WorkflowActionType action, string? comment, CancellationToken ct = default);

    /// <summary>
    /// Sets a stage's rules: its condition (validated, <c>Workflow.ConditionInvalid</c>), a role whose holders may approve, and a dynamic
    /// approver such as the requester's manager.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="definitionId">The workflow definition.</param>
    /// <param name="sequence">The stage.</param>
    /// <param name="condition">The condition, or null.</param>
    /// <param name="roleName">The approving role, or null.</param>
    /// <param name="dynamicApprover">The dynamic approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetStageRulesAsync(long companyId, long definitionId, int sequence, string? condition, string? roleName, One4All.Core.Workflow.DynamicApprover dynamicApprover, CancellationToken ct = default);

    /// <summary>
    /// Defines the workflow for a document type (brief §7: stages, quorum, value bands, conditions, approvers by user, role or the requester's manager,
    /// SLA escalation). One definition is active per document type: an earlier one is kept for the documents already routed through it but switched off.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new definition's id, or why not (<c>Workflow.DefinitionInvalid</c>, <c>Workflow.RoleNotFound</c>, <c>Workflow.UserNotFound</c>, <c>Workflow.ConditionInvalid</c>).</returns>
    Task<Result<long>> DefineAsync(long companyId, WorkflowDefinitionInput definition, CancellationToken ct = default);

    /// <summary>Switches a definition on or off (off: new documents of its type are not routed; those in flight finish).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="definitionId">The definition.</param>
    /// <param name="active">On or off.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SetDefinitionActiveAsync(long companyId, long definitionId, bool active, CancellationToken ct = default);
}

/// <summary>A stage of a workflow being defined.</summary>
/// <param name="Sequence">Its order (1, 2, …).</param>
/// <param name="Name">What the stage is called (Buyer, Finance, CFO).</param>
/// <param name="Quorum">How many approvals it takes.</param>
/// <param name="MinValue">The document value from which the stage applies (null: always).</param>
/// <param name="Condition">A condition on the document's attributes (e.g. Department = IT and Value &gt; 5000), or null.</param>
/// <param name="RoleName">A role whose holders may approve, or null.</param>
/// <param name="Approvers">User names who may approve.</param>
/// <param name="DynamicApprover">A dynamic approver (the requester's manager).</param>
/// <param name="EscalateAfterHours">Hours before the stage escalates, or null.</param>
/// <param name="EscalateTo">The user name escalated to, or null.</param>
/// <param name="GroupName">A user group whose members may approve, or null.</param>
public sealed record WorkflowStageInput(int Sequence, string Name, int Quorum = 1, decimal? MinValue = null, string? Condition = null, string? RoleName = null, IReadOnlyList<string>? Approvers = null,
    One4All.Core.Workflow.DynamicApprover DynamicApprover = One4All.Core.Workflow.DynamicApprover.None, int? EscalateAfterHours = null, string? EscalateTo = null, string? GroupName = null,
    string? ParallelGroups = null);

/// <summary>A workflow definition being saved.</summary>
/// <param name="DocumentType">The document type it routes (<see cref="WorkflowDocumentTypes"/>, or a custom one).</param>
/// <param name="Name">What the workflow is called.</param>
/// <param name="Stages">Its stages in order.</param>
/// <param name="Active">Whether it routes documents from now (default yes).</param>
public sealed record WorkflowDefinitionInput(string DocumentType, string Name, IReadOnlyList<WorkflowStageInput> Stages, bool Active = true);
