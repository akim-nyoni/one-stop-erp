using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;

namespace One4All.Infrastructure.Workflow;

/// <summary>Scheduled task: escalates approvals waiting past their stage's time limit (brief §7), rather than only when someone asks.</summary>
public sealed class WorkflowEscalationTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly IWorkflowService _workflow;

    /// <summary>Creates the task.</summary>
    /// <param name="workflow">The workflow engine.</param>
    public WorkflowEscalationTask(IWorkflowService workflow) => _workflow = workflow;

    /// <inheritdoc />
    public string Key => "workflow.escalate";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
        => $"{await _workflow.EscalateOverdueAsync(companyId, ct).ConfigureAwait(false)} overdue approval(s) escalated.";
}
