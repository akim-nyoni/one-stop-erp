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
    public async Task<Result<WorkflowStartResult>> StartAsync(long companyId, string documentType, string documentReference, decimal value, long initiatedByUserId, CancellationToken ct = default)
    {
        WorkflowDefinition? definition = await _db.WorkflowDefinitions
            .Include(d => d.Stages)
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.DocumentType == documentType && d.IsActive, ct)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return Error.NotFound("Workflow.NoDefinition", $"No active workflow is defined for '{documentType}'.");
        }

        List<WorkflowStage> applicable = definition.Stages.Where(s => s.AppliesTo(value)).OrderBy(s => s.Sequence).ToList();
        int firstSequence = applicable.Count > 0 ? applicable[0].Sequence : 0;

        WorkflowInstance instance = new(companyId, definition.Id, documentType, documentReference, value, initiatedByUserId, firstSequence);
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

            WorkflowStage? stage = definition.Stages
                .Where(s => s.AppliesTo(instance.Value))
                .FirstOrDefault(s => s.Sequence == instance.CurrentStageSequence);
            if (stage is null)
            {
                continue;
            }

            // Eligible when the stage names this user, or names nobody (anyone may act).
            bool eligible = stage.EligibleUserIds.Count == 0 || stage.EligibleUserIds.Contains(userId);
            bool alreadyApproved = instance.Approvals.Any(a => a.StageSequence == stage.Sequence && a.UserId == userId && a.Action == WorkflowActionType.Approve);
            if (!eligible || alreadyApproved)
            {
                continue;
            }

            int approvalsSoFar = instance.Approvals.Count(a => a.StageSequence == stage.Sequence && a.Action == WorkflowActionType.Approve);
            pending.Add(new PendingApproval(instance.Id, instance.DocumentType, instance.DocumentReference, instance.Value, instance.InitiatedByUserId, stage.Sequence, stage.Name, approvalsSoFar, stage.Quorum));
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

        List<WorkflowStage> applicable = definition.Stages.Where(s => s.AppliesTo(instance.Value)).OrderBy(s => s.Sequence).ToList();
        WorkflowStage? currentStage = applicable.FirstOrDefault(s => s.Sequence == instance.CurrentStageSequence);

        if (currentStage is not null && currentStage.EligibleUserIds.Count > 0 && !currentStage.EligibleUserIds.Contains(userId))
        {
            return Error.Forbidden("Workflow.NotEligible", $"User {userId} may not approve stage '{currentStage.Name}'.");
        }

        Result<WorkflowStatus> result = instance.Act(applicable, userId, action, comment, _clock.UtcNow);
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
}
