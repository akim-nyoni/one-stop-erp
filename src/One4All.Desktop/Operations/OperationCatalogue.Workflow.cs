using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Workflow;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Approval workflow operations: delegation, added approvers and escalation.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Workflow()
    {
        const string module = "Approvals";
        yield return new Operation
        {
            Module = module, Title = "Delegate my approvals", ListTitle = "Approval delegations",
            Description = "While an approver is away, the delegate may approve anything they could, recorded as on their behalf.",
            Fields = [Req("From", "Approver who is away (user name)"), Req("To", "Delegate (user name)"), Today("Starts", "From"), Today("Ends", "To"), Req("Reason", "Reason", "Leave")],
            Run = async (c, i) => Check(await c.Get<IWorkflowService>().DelegateAsync(c.CompanyId, await UserAsync(c, i.Text("From")).ConfigureAwait(true), await UserAsync(c, i.Text("To")).ConfigureAwait(true),
                i.Date("Starts"), i.Date("Ends"), i.Text("Reason")).ConfigureAwait(true), id => $"Delegation {id} recorded."),
            List = DelegationsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Add an approver to a document", ListTitle = "Waiting on me",
            Description = "Adds someone to the current stage of one document's approval, alongside the stage's own approvers.",
            Fields = [Num("Instance", "Workflow instance id"), Req("User", "Approver (user name)")],
            Run = async (c, i) => Check(await c.Get<IWorkflowService>().AddApproverAsync((long)i.Number("Instance"), await UserAsync(c, i.Text("User")).ConfigureAwait(true), c.UserId).ConfigureAwait(true),
                "Approver added."),
            List = WaitingOnMeAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Escalate overdue approvals", ListTitle = "Waiting on me",
            Description = "Adds each overdue stage's escalation user to documents that have waited longer than the stage allows.",
            Fields = [],
            Run = async (c, i) => $"{await c.Get<IWorkflowService>().EscalateOverdueAsync(c.CompanyId).ConfigureAwait(true)} document(s) escalated.",
            List = WaitingOnMeAsync,
        };
    }

    private static async Task<DataTable> WaitingOnMeAsync(OperationContext c)
        => Table(await c.Get<IWorkflowService>().PendingForUserAsync(c.CompanyId, c.UserId).ConfigureAwait(true),
            ("Instance", p => p.InstanceId), ("Document", p => $"{p.DocumentType} {p.DocumentReference}"), ("Value", p => M(p.Value)), ("Stage", p => p.StageName),
            ("Approvals", p => $"{p.ApprovalsSoFar}/{p.Quorum}"), ("On behalf of", p => p.OnBehalfOfUserId));

    private static async Task<DataTable> DelegationsAsync(OperationContext c)
    {
        Dictionary<long, string> users = await c.Db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username).ConfigureAwait(true);
        return Table(await c.Db.ApprovalDelegations.AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.StartsOn).ToListAsync().ConfigureAwait(true),
            ("From", d => users.GetValueOrDefault(d.FromUserId)), ("To", d => users.GetValueOrDefault(d.ToUserId)), ("Starts", d => D(d.StartsOn)), ("Ends", d => D(d.EndsOn)), ("Reason", d => d.Reason),
            ("Withdrawn", d => d.Withdrawn));
    }
}
