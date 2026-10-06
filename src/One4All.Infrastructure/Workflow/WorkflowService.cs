using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Workflow;

/// <summary>The generic workflow/approval service (brief §7), persisting instances and routing approvals.</summary>
public sealed class WorkflowService : IWorkflowService
{
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    public WorkflowService(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result<WorkflowStartResult>> StartAsync(long companyId, string documentType, string documentReference, decimal value, long initiatedByUserId, CancellationToken ct = default)
        => StartAsync(companyId, documentType, documentReference, value, initiatedByUserId, new Dictionary<string, string>(), ct);

    /// <inheritdoc />
    public async Task<Result<WorkflowStartResult>> StartAsync(long companyId, string documentType, string documentReference, decimal value, long initiatedByUserId,
        IReadOnlyDictionary<string, string> attributes, CancellationToken ct = default)
    {
        WorkflowDefinition? definition = await _db.WorkflowDefinitions
            .Include(d => d.Stages)
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.DocumentType == documentType && d.IsActive, ct)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return Error.NotFound("Workflow.NoDefinition", $"No active workflow is defined for '{documentType}'.");
        }

        List<WorkflowStage> applicable = Applicable(definition, value, attributes);
        int firstSequence = applicable.Count > 0 ? applicable[0].Sequence : 0;

        WorkflowInstance instance = new(companyId, definition.Id, documentType, documentReference, value, initiatedByUserId, firstSequence)
        {
            AttributesJson = attributes.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(attributes),
        };
        _db.WorkflowInstances.Add(instance);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new WorkflowStartResult(instance.Id, instance.Status);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingApproval>> PendingForUserAsync(long companyId, long userId, CancellationToken ct = default)
    {
        List<WorkflowInstance> inProgress = await _db.WorkflowInstances
            .Include(i => i.Approvals)
            .Where(i => i.CompanyId == companyId && i.Status == WorkflowStatus.InProgress)
            .OrderBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        if (inProgress.Count == 0)
        {
            return [];
        }

        List<long> definitionIds = inProgress.Select(i => i.WorkflowDefinitionId).Distinct().ToList();
        Dictionary<long, WorkflowDefinition> definitions = await _db.WorkflowDefinitions
            .Include(d => d.Stages)
            .Where(d => definitionIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct).ConfigureAwait(false);

        List<PendingApproval> pending = [];
        foreach (WorkflowInstance instance in inProgress)
        {
            if (!definitions.TryGetValue(instance.WorkflowDefinitionId, out WorkflowDefinition? definition))
            {
                continue;
            }

            WorkflowStage? stage = Applicable(definition, instance.Value, Attributes(instance)).FirstOrDefault(s => s.Sequence == instance.CurrentStageSequence);
            if (stage is null)
            {
                continue;
            }

            (bool eligible, long? onBehalfOf) = await EligibilityAsync(instance, stage, userId, ct).ConfigureAwait(false);
            if (!eligible || AlreadyApproved(instance, stage.Sequence, userId, onBehalfOf))
            {
                continue;
            }

            int approvalsSoFar = instance.Approvals.Count(a => a.StageSequence == stage.Sequence && a.Action == WorkflowActionType.Approve);
            pending.Add(new PendingApproval(instance.Id, instance.DocumentType, instance.DocumentReference, instance.Value, instance.InitiatedByUserId, stage.Sequence, stage.Name, approvalsSoFar, stage.Quorum,
                onBehalfOf));
        }

        return pending;
    }

    /// <inheritdoc />
    public async Task<Result<WorkflowStatus>> ActAsync(long instanceId, long userId, WorkflowActionType action, string? comment, CancellationToken ct = default)
    {
        WorkflowInstance? instance = await _db.WorkflowInstances
            .Include(i => i.Approvals)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false);

        if (instance is null)
        {
            return Error.NotFound("Workflow.InstanceNotFound", $"Workflow instance {instanceId} not found.");
        }

        WorkflowDefinition? definition = await _db.WorkflowDefinitions
            .Include(d => d.Stages)
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, ct)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return Error.NotFound("Workflow.NoDefinition", "The workflow definition was not found.");
        }

        List<WorkflowStage> applicable = Applicable(definition, instance.Value, Attributes(instance));
        WorkflowStage? currentStage = applicable.FirstOrDefault(s => s.Sequence == instance.CurrentStageSequence);

        long? onBehalfOf = null;
        if (currentStage is not null)
        {
            (bool eligible, onBehalfOf) = await EligibilityAsync(instance, currentStage, userId, ct).ConfigureAwait(false);
            if (!eligible)
            {
                return Error.Forbidden("Workflow.NotEligible", $"User {userId} may not approve stage '{currentStage.Name}'.");
            }

            if (action == WorkflowActionType.Approve && AlreadyApproved(instance, currentStage.Sequence, userId, onBehalfOf))
            {
                return Error.Conflict("Workflow.AlreadyApproved", "That approval has already been given at this stage.");
            }
        }

        Result<WorkflowStatus> result = instance.Act(applicable, userId, action, comment, _clock.UtcNow, onBehalfOf);
        if (result.IsSuccess && instance.Approvals.Count > 0)
        {
            // The document as it stood at this decision.
            instance.Approvals[^1].Snapshot = System.Text.Json.JsonSerializer.Serialize(new
            {
                instance.DocumentType, instance.DocumentReference, instance.Value, Stage = currentStage?.Name, Attributes = Attributes(instance),
            });
        }
        if (result.IsFailure)
        {
            return result;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return result.Value;
    }

    /// <inheritdoc />
    public async Task<Result<WorkflowStatus>> GetStatusAsync(long instanceId, CancellationToken ct = default)
    {
        WorkflowInstance? instance = await _db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct).ConfigureAwait(false);
        return instance is null
            ? Error.NotFound("Workflow.InstanceNotFound", $"Workflow instance {instanceId} not found.")
            : instance.Status;
    }

    /// <inheritdoc />
    public async Task<Result<long>> DelegateAsync(long companyId, long fromUserId, long toUserId, DateOnly startsOn, DateOnly endsOn, string reason, CancellationToken ct = default)
    {
        if (fromUserId == toUserId || endsOn < startsOn || string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Workflow.DelegationInvalid", "A delegation is to someone else, for a period, with a reason.");
        }

        if (await _db.ApprovalDelegations.AnyAsync(d => d.CompanyId == companyId && d.FromUserId == fromUserId && !d.Withdrawn && d.StartsOn <= endsOn && d.EndsOn >= startsOn, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Workflow.DelegationOverlap", "The approver already has a delegation covering some of those dates.");
        }

        ApprovalDelegation delegation = new(companyId, fromUserId, toUserId, startsOn, endsOn, reason.Trim());
        _db.ApprovalDelegations.Add(delegation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return delegation.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AddApproverAsync(long instanceId, long userId, long addedByUserId, CancellationToken ct = default)
    {
        WorkflowInstance? instance = await _db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct).ConfigureAwait(false);
        if (instance is null || instance.Status != WorkflowStatus.InProgress)
        {
            return Result.Failure(Error.Rule("Workflow.NotInProgress", "Approvers can only be added to a workflow in progress."));
        }

        if (await _db.WorkflowInstanceApprovers.AnyAsync(a => a.WorkflowInstanceId == instanceId && a.StageSequence == instance.CurrentStageSequence && a.UserId == userId, ct).ConfigureAwait(false))
        {
            return Result.Success();
        }

        _db.WorkflowInstanceApprovers.Add(new WorkflowInstanceApprover(instanceId, instance.CurrentStageSequence, userId, AddedApproverReason.AdHoc, addedByUserId, _clock.UtcNow));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<int> EscalateOverdueAsync(long companyId, CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.UtcNow;
        List<WorkflowInstance> open = await _db.WorkflowInstances.Where(i => i.CompanyId == companyId && i.Status == WorkflowStatus.InProgress).ToListAsync(ct).ConfigureAwait(false);
        List<long> definitionIds = [.. open.Select(i => i.WorkflowDefinitionId).Distinct()];
        Dictionary<long, WorkflowDefinition> definitions = await _db.WorkflowDefinitions.Include(d => d.Stages).Where(d => definitionIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct).ConfigureAwait(false);
        List<long> ids = [.. open.Select(i => i.Id)];
        List<WorkflowInstanceApprover> added = await _db.WorkflowInstanceApprovers.Where(a => ids.Contains(a.WorkflowInstanceId) && a.Reason == AddedApproverReason.Escalated).ToListAsync(ct).ConfigureAwait(false);
        int escalated = 0;
        foreach (WorkflowInstance instance in open)
        {
            WorkflowStage? stage = definitions.GetValueOrDefault(instance.WorkflowDefinitionId)?.Stages.FirstOrDefault(s => s.Sequence == instance.CurrentStageSequence);
            DateTimeOffset entered = instance.StageEnteredOnUtc ?? instance.CreatedOnUtc;
            if (stage is not { EscalateAfterHours: int hours, EscalateToUserId: long to } || now - entered < TimeSpan.FromHours(hours)
                || added.Any(a => a.WorkflowInstanceId == instance.Id && a.StageSequence == stage.Sequence && a.AddedOnUtc >= entered))
            {
                continue;
            }

            _db.WorkflowInstanceApprovers.Add(new WorkflowInstanceApprover(instance.Id, stage.Sequence, to, AddedApproverReason.Escalated, null, now));
            escalated++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return escalated;
    }

    // Whether a user may act at a stage: named on it (or it names nobody), added to this instance's stage, or the delegate of someone who
    // may — then acting on their behalf.
    private async Task<(bool Eligible, long? OnBehalfOf)> EligibilityAsync(WorkflowInstance instance, WorkflowStage stage, long userId, CancellationToken ct)
    {
        List<long> extra = await _db.WorkflowInstanceApprovers.Where(a => a.WorkflowInstanceId == instance.Id && a.StageSequence == stage.Sequence).Select(a => a.UserId).ToListAsync(ct).ConfigureAwait(false);

        // Holders of the stage's role, and the requester's manager when the stage asks for them, may approve too.
        if (!string.IsNullOrWhiteSpace(stage.RequiredRoleName))
        {
            string role = stage.RequiredRoleName.Trim().ToUpperInvariant();
            extra.AddRange(await (from ur in _db.Set<Core.Security.UserRole>()
                                  join r in _db.Roles on ur.RoleId equals r.Id
                                  where r.NormalizedName == role
                                  select ur.UserId).ToListAsync(ct).ConfigureAwait(false));
        }

        bool dynamicOnly = false;
        if (stage.DynamicApprover == DynamicApprover.RequesterManager)
        {
            long? manager = await _db.Users.Where(u => u.Id == instance.InitiatedByUserId).Select(u => u.ManagerUserId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (manager is long m)
            {
                extra.Add(m);
            }

            dynamicOnly = true;
        }

        bool open = stage.EligibleUserIds.Count == 0 && extra.Count == 0 && string.IsNullOrWhiteSpace(stage.RequiredRoleName) && !dynamicOnly;
        bool Direct(long user) => open || stage.EligibleUserIds.Contains(user) || extra.Contains(user);
        if (Direct(userId))
        {
            return (true, null);
        }

        DateOnly today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        List<ApprovalDelegation> covering = await _db.ApprovalDelegations.Where(d => d.CompanyId == instance.CompanyId && d.ToUserId == userId && !d.Withdrawn && d.StartsOn <= today && d.EndsOn >= today)
            .ToListAsync(ct).ConfigureAwait(false);
        ApprovalDelegation? delegation = covering.FirstOrDefault(d => Direct(d.FromUserId) && !AlreadyApproved(instance, stage.Sequence, d.FromUserId, null));
        return delegation is null ? (false, null) : (true, delegation.FromUserId);
    }

    // An approval counts for the person it was given by or on behalf of.
    private static bool AlreadyApproved(WorkflowInstance instance, int stage, long userId, long? onBehalfOf)
        => instance.Approvals.Any(a => a.StageSequence == stage && a.Action == WorkflowActionType.Approve
            && (a.UserId == userId || a.OnBehalfOfUserId == userId || (onBehalfOf is long principal && (a.UserId == principal || a.OnBehalfOfUserId == principal))));

    /// <inheritdoc />
    public async Task<IReadOnlyList<BulkActionResult>> ActManyAsync(IReadOnlyList<long> instanceIds, long userId, WorkflowActionType action, string? comment, CancellationToken ct = default)
    {
        List<BulkActionResult> results = [];
        foreach (long id in instanceIds.Distinct())
        {
            Result<WorkflowStatus> acted = await ActAsync(id, userId, action, comment, ct).ConfigureAwait(false);
            results.Add(acted.IsSuccess ? new BulkActionResult(id, true, acted.Value, null) : new BulkActionResult(id, false, null, acted.Error.Message));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<Result> SetStageRulesAsync(long companyId, long definitionId, int sequence, string? condition, string? roleName, DynamicApprover dynamicApprover, CancellationToken ct = default)
    {
        if (Application.Workflow.WorkflowCondition.Problem(condition) is string problem)
        {
            return Result.Failure(Error.Validation("Workflow.ConditionInvalid", problem));
        }

        WorkflowDefinition? definition = await _db.WorkflowDefinitions.Include(d => d.Stages).FirstOrDefaultAsync(d => d.Id == definitionId && d.CompanyId == companyId, ct).ConfigureAwait(false);
        WorkflowStage? stage = definition?.Stages.FirstOrDefault(s => s.Sequence == sequence);
        if (stage is null)
        {
            return Result.Failure(Error.NotFound("Workflow.StageNotFound", $"Stage {sequence} not found."));
        }

        if (!string.IsNullOrWhiteSpace(roleName) && !await _db.Roles.AnyAsync(r => r.NormalizedName == roleName.Trim().ToUpperInvariant(), ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Workflow.RoleNotFound", $"Role {roleName} not found."));
        }

        stage.Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        stage.RequiredRoleName = string.IsNullOrWhiteSpace(roleName) ? null : roleName.Trim();
        stage.DynamicApprover = dynamicApprover;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // The stages that apply to a document: within their value band and with their condition holding.
    private static List<WorkflowStage> Applicable(WorkflowDefinition definition, decimal value, IReadOnlyDictionary<string, string> attributes)
        => [.. definition.Stages.Where(s => s.AppliesTo(value) && Application.Workflow.WorkflowCondition.Matches(s.Condition, value, attributes)).OrderBy(s => s.Sequence)];

    private static IReadOnlyDictionary<string, string> Attributes(WorkflowInstance instance)
        => string.IsNullOrWhiteSpace(instance.AttributesJson) ? new Dictionary<string, string>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(instance.AttributesJson) ?? [];
}
