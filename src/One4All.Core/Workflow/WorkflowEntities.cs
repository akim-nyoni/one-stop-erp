using One4All.Core.Common;

namespace One4All.Core.Workflow;

/// <summary>The status of a workflow instance (brief §7).</summary>
public enum WorkflowStatus
{
    /// <summary>Awaiting approval at some stage.</summary>
    InProgress = 0,

    /// <summary>Fully approved.</summary>
    Approved = 1,

    /// <summary>Rejected - the document is refused.</summary>
    Rejected = 2,

    /// <summary>Returned to the originator for amendment.</summary>
    Returned = 3,

    /// <summary>Cancelled.</summary>
    Cancelled = 4,
}

/// <summary>An action a user can take on a workflow instance (brief §7).</summary>
public enum WorkflowActionType
{
    /// <summary>Approve at the current stage.</summary>
    Approve = 1,

    /// <summary>Reject the document.</summary>
    Reject = 2,

    /// <summary>Return the document for amendment.</summary>
    Return = 3,
}

/// <summary>
/// A workflow definition for a document type (<c>wf.definition</c>, brief §7): an ordered set of approval
/// stages. One generic engine drives requisitions, POs, journals, payment runs, budgets, master-data
/// changes and more.
/// </summary>
public sealed class WorkflowDefinition : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<WorkflowStage> _stages = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WorkflowDefinition()
    {
        DocumentType = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a workflow definition.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="documentType">The document type this workflow approves (e.g. <c>Requisition</c>).</param>
    /// <param name="name">The definition name.</param>
    public WorkflowDefinition(long companyId, string documentType, string name)
    {
        CompanyId = companyId;
        DocumentType = documentType;
        Name = name;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The document type this workflow approves.</summary>
    public string DocumentType { get; private set; }

    /// <summary>The definition name.</summary>
    public string Name { get; set; }

    /// <summary>Whether the definition is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>The ordered approval stages.</summary>
    public IReadOnlyList<WorkflowStage> Stages => _stages.OrderBy(s => s.Sequence).ToList();

    /// <summary>Adds a stage.</summary>
    /// <param name="sequence">The 1-based stage order.</param>
    /// <param name="name">The stage name.</param>
    /// <param name="quorum">Approvals required to pass the stage.</param>
    /// <param name="minValue">Only apply this stage when the document value is at least this (value-band routing); null = always.</param>
    /// <returns>The created stage.</returns>
    public WorkflowStage AddStage(int sequence, string name, int quorum = 1, decimal? minValue = null)
    {
        WorkflowStage stage = new(sequence, name, quorum, minValue);
        _stages.Add(stage);
        return stage;
    }
}

/// <summary>An approval stage of a <see cref="WorkflowDefinition"/> (<c>wf.stage</c>).</summary>
public sealed class WorkflowStage : Entity
{
    private readonly List<long> _eligibleUserIds = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WorkflowStage() => Name = string.Empty;

    /// <summary>Creates a stage.</summary>
    /// <param name="sequence">The 1-based order.</param>
    /// <param name="name">The stage name.</param>
    /// <param name="quorum">Approvals required.</param>
    /// <param name="minValue">Value-band threshold.</param>
    public WorkflowStage(int sequence, string name, int quorum, decimal? minValue)
    {
        Sequence = sequence;
        Name = name;
        Quorum = Math.Max(1, quorum);
        MinValue = minValue;
    }

    /// <summary>The owning definition id.</summary>
    public long WorkflowDefinitionId { get; private set; }

    /// <summary>The 1-based stage order.</summary>
    public int Sequence { get; private set; }

    /// <summary>The stage name.</summary>
    public string Name { get; private set; }

    /// <summary>Approvals required to pass the stage (quorum, e.g. any 2 of 3).</summary>
    public int Quorum { get; private set; }

    /// <summary>Only applies when the document value is at least this; null = always applies.</summary>
    public decimal? MinValue { get; private set; }

    /// <summary>A role name whose holders may approve this stage (optional).</summary>
    public string? RequiredRoleName { get; set; }

    /// <summary>A user group whose members may approve (brief §7 approvers by group), or null.</summary>
    public string? RequiredGroupName { get; set; }

    /// <summary>Gets or sets the condition (over the document's value and attributes) for the stage to apply; null: always (value bands aside).</summary>
    public string? Condition { get; set; }

    /// <summary>Gets or sets a dynamic approver resolved per document (e.g. the requester's manager).</summary>
    public DynamicApprover DynamicApprover { get; set; }

    /// <summary>Explicit eligible approver user ids (in addition to the required role).</summary>
    public IReadOnlyList<long> EligibleUserIds => _eligibleUserIds.AsReadOnly();

    /// <summary>Adds an eligible approver.</summary>
    /// <param name="userId">The user id.</param>
    public void AddEligibleUser(long userId)
    {
        if (!_eligibleUserIds.Contains(userId))
        {
            _eligibleUserIds.Add(userId);
        }
    }

    /// <summary>True when this stage applies to a document of the given value.</summary>
    /// <param name="value">The document value.</param>
    /// <returns>True when applicable.</returns>
    public bool AppliesTo(decimal value) => MinValue is null || value >= MinValue.Value;

    /// <summary>Gets or sets how many hours an instance may wait at this stage before it escalates.</summary>
    public int? EscalateAfterHours { get; set; }

    /// <summary>Gets or sets who it escalates to (added as an approver of the stage).</summary>
    public long? EscalateToUserId { get; set; }
}

/// <summary>Why an approver was added to an instance.</summary>
public enum AddedApproverReason
{
    /// <summary>Added by hand to this document.</summary>
    AdHoc = 1,

    /// <summary>The stage waited too long and escalated.</summary>
    Escalated = 2,
}

/// <summary>An approver added to one instance's stage (<c>wf.instance_approver</c>): by hand, or by escalation.</summary>
public sealed class WorkflowInstanceApprover : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WorkflowInstanceApprover()
    {
    }

    /// <summary>Creates the approver.</summary>
    /// <param name="instanceId">The instance.</param>
    /// <param name="stageSequence">The stage.</param>
    /// <param name="userId">The approver.</param>
    /// <param name="reason">Why.</param>
    /// <param name="addedByUserId">Who added them (null: the escalation sweep).</param>
    /// <param name="addedOnUtc">When.</param>
    public WorkflowInstanceApprover(long instanceId, int stageSequence, long userId, AddedApproverReason reason, long? addedByUserId, DateTimeOffset addedOnUtc)
    {
        WorkflowInstanceId = instanceId;
        StageSequence = stageSequence;
        UserId = userId;
        Reason = reason;
        AddedByUserId = addedByUserId;
        AddedOnUtc = addedOnUtc;
    }

    /// <summary>The instance.</summary>
    public long WorkflowInstanceId { get; private set; }

    /// <summary>The stage.</summary>
    public int StageSequence { get; private set; }

    /// <summary>The approver.</summary>
    public long UserId { get; private set; }

    /// <summary>Why.</summary>
    public AddedApproverReason Reason { get; private set; }

    /// <summary>Who added them.</summary>
    public long? AddedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset AddedOnUtc { get; private set; }
}

/// <summary>
/// An approver's delegation while away (<c>wf.delegation</c>): between the dates the delegate may act on anything the approver could,
/// recorded as acting on the approver's behalf.
/// </summary>
public sealed class ApprovalDelegation : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ApprovalDelegation() => Reason = string.Empty;

    /// <summary>Creates the delegation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromUserId">The approver who is away.</param>
    /// <param name="toUserId">The delegate.</param>
    /// <param name="startsOn">The first day.</param>
    /// <param name="endsOn">The last day.</param>
    /// <param name="reason">Why (leave, travel).</param>
    public ApprovalDelegation(long companyId, long fromUserId, long toUserId, DateOnly startsOn, DateOnly endsOn, string reason)
    {
        CompanyId = companyId;
        FromUserId = fromUserId;
        ToUserId = toUserId;
        StartsOn = startsOn;
        EndsOn = endsOn;
        Reason = reason;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The approver who is away.</summary>
    public long FromUserId { get; private set; }

    /// <summary>The delegate.</summary>
    public long ToUserId { get; private set; }

    /// <summary>The first day.</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>The last day.</summary>
    public DateOnly EndsOn { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets or sets a value indicating whether it was withdrawn early.</summary>
    public bool Withdrawn { get; set; }

    /// <summary>Whether it is in force on a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>True when in force.</returns>
    public bool InForce(DateOnly date) => !Withdrawn && date >= StartsOn && date <= EndsOn;
}

/// <summary>An approver worked out from the document rather than named on the stage.</summary>
public enum DynamicApprover
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>The manager of whoever started the workflow.</summary>
    RequesterManager = 1,
}

