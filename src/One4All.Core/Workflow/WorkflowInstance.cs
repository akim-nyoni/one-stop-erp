using One4All.Core.Common;

namespace One4All.Core.Workflow;

/// <summary>
/// A running workflow for one document (<c>wf.instance</c>, brief §7). Tracks the current stage, the
/// approvals recorded, and the final status. The advancement logic (quorum, value-band stage skipping,
/// reject/return) lives here so it is exhaustively unit-testable.
/// </summary>
public sealed class WorkflowInstance : AuditableEntity, ICompanyScoped
{
    private readonly List<WorkflowApproval> _approvals = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WorkflowInstance()
    {
        DocumentType = string.Empty;
        DocumentReference = string.Empty;
    }

    /// <summary>Creates and starts an instance at the first applicable stage.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="definitionId">The workflow definition.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="documentReference">The document reference/number.</param>
    /// <param name="value">The document value (for value-band routing).</param>
    /// <param name="initiatedByUserId">Who started the workflow.</param>
    /// <param name="firstStageSequence">The first applicable stage's sequence, or 0 if none applies (auto-approved).</param>
    public WorkflowInstance(long companyId, long definitionId, string documentType, string documentReference, decimal value, long initiatedByUserId, int firstStageSequence)
    {
        CompanyId = companyId;
        WorkflowDefinitionId = definitionId;
        DocumentType = documentType;
        DocumentReference = documentReference;
        Value = value;
        InitiatedByUserId = initiatedByUserId;
        CurrentStageSequence = firstStageSequence;
        Status = firstStageSequence == 0 ? WorkflowStatus.Approved : WorkflowStatus.InProgress;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The workflow definition id.</summary>
    public long WorkflowDefinitionId { get; private set; }

    /// <summary>The document type.</summary>
    public string DocumentType { get; private set; }

    /// <summary>The document reference/number.</summary>
    public string DocumentReference { get; private set; }

    /// <summary>The document value.</summary>
    public decimal Value { get; private set; }

    /// <summary>Who started the workflow.</summary>
    public long InitiatedByUserId { get; private set; }

    /// <summary>The current stage sequence (0 when finished with no stages).</summary>
    public int CurrentStageSequence { get; private set; }

    /// <summary>The status.</summary>
    public WorkflowStatus Status { get; private set; }

    /// <summary>The approvals/actions recorded.</summary>
    public IReadOnlyList<WorkflowApproval> Approvals => _approvals.AsReadOnly();

    /// <summary>Gets or sets the document's attributes (JSON object of name → text) that stage conditions read.</summary>
    public string? AttributesJson { get; set; }

    /// <summary>When the instance entered its current stage (escalation runs from here; null: since it was created).</summary>
    public DateTimeOffset? StageEnteredOnUtc { get; private set; }

    /// <summary>
    /// Records an action by a user and advances the workflow. Eligibility must be checked by the caller.
    /// </summary>
    /// <param name="applicableStages">The applicable stages (already filtered by value band), ordered by sequence.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="action">The action.</param>
    /// <param name="comment">An optional comment (mandatory for reject/return by policy).</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The updated status, or a rule error.</returns>
    /// <param name="onBehalfOfUserId">The approver a delegate acts for, if any.</param>
    public Result<WorkflowStatus> Act(IReadOnlyList<WorkflowStage> applicableStages, long userId, WorkflowActionType action, string? comment, DateTimeOffset nowUtc, long? onBehalfOfUserId = null)
    {
        if (Status != WorkflowStatus.InProgress)
        {
            return Error.Rule("Workflow.NotInProgress", $"The workflow is {Status} and cannot be actioned.");
        }

        WorkflowStage? currentStage = applicableStages.FirstOrDefault(s => s.Sequence == CurrentStageSequence);
        if (currentStage is null)
        {
            return Error.Rule("Workflow.NoCurrentStage", "The current stage could not be resolved.");
        }

        if ((action is WorkflowActionType.Reject or WorkflowActionType.Return) && string.IsNullOrWhiteSpace(comment))
        {
            return Error.Validation("Workflow.CommentRequired", "A comment is required to reject or return.");
        }

        _approvals.Add(new WorkflowApproval(CurrentStageSequence, userId, action, comment, nowUtc) { OnBehalfOfUserId = onBehalfOfUserId });

        switch (action)
        {
            case WorkflowActionType.Reject:
                Status = WorkflowStatus.Rejected;
                break;

            case WorkflowActionType.Return:
                Status = WorkflowStatus.Returned;
                break;

            case WorkflowActionType.Approve:
                int approvalsAtStage = _approvals.Count(a => a.StageSequence == CurrentStageSequence && a.Action == WorkflowActionType.Approve);
                if (approvalsAtStage >= currentStage.Quorum)
                {
                    WorkflowStage? next = applicableStages.FirstOrDefault(s => s.Sequence > CurrentStageSequence);
                    if (next is null)
                    {
                        CurrentStageSequence = 0;
                        Status = WorkflowStatus.Approved;
                    }
                    else
                    {
                        CurrentStageSequence = next.Sequence;
                        StageEnteredOnUtc = nowUtc;
                    }
                }

                break;
        }

        return Status;
    }

    /// <summary>Resubmits a returned workflow to the first applicable stage.</summary>
    /// <param name="firstStageSequence">The first applicable stage sequence.</param>
    /// <param name="nowUtc">When it was resubmitted (escalation restarts from here).</param>
    public void Resubmit(int firstStageSequence, DateTimeOffset? nowUtc = null)
    {
        if (Status == WorkflowStatus.Returned)
        {
            CurrentStageSequence = firstStageSequence;
            StageEnteredOnUtc = nowUtc;
            Status = firstStageSequence == 0 ? WorkflowStatus.Approved : WorkflowStatus.InProgress;
        }
    }
}

/// <summary>A recorded action on a workflow (<c>wf.approval</c>, brief §7 — full history).</summary>
public sealed class WorkflowApproval : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WorkflowApproval()
    {
    }

    /// <summary>Creates an approval record.</summary>
    /// <param name="stageSequence">The stage acted on.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="action">The action.</param>
    /// <param name="comment">An optional comment.</param>
    /// <param name="occurredOnUtc">When it occurred.</param>
    public WorkflowApproval(int stageSequence, long userId, WorkflowActionType action, string? comment, DateTimeOffset occurredOnUtc)
    {
        StageSequence = stageSequence;
        UserId = userId;
        Action = action;
        Comment = comment;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>The owning instance id.</summary>
    public long WorkflowInstanceId { get; private set; }

    /// <summary>The stage acted on.</summary>
    public int StageSequence { get; private set; }

    /// <summary>The acting user.</summary>
    public long UserId { get; private set; }

    /// <summary>The action.</summary>
    public WorkflowActionType Action { get; private set; }

    /// <summary>An optional comment.</summary>
    public string? Comment { get; private set; }

    /// <summary>When it occurred.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>Gets the approver a delegate acted for, if any.</summary>
    public long? OnBehalfOfUserId { get; init; }

    /// <summary>Gets or sets the document as it stood when this decision was made (JSON).</summary>
    public string? Snapshot { get; set; }

    /// <summary>Gets or sets the SHA-256 (hex) of <see cref="Snapshot"/>, so a later change to the stored snapshot shows.</summary>
    public string? SnapshotHash { get; set; }

    /// <summary>Gets or sets the terminal or network address the decision was given from.</summary>
    public string? Machine { get; set; }
}
