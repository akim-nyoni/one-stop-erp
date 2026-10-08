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
            Module = module, Title = "Request a master-data change", ListTitle = "Master-data changes",
            Description = "Changes a tariff's rates or a customer's, supplier's or item's field — held for the \"MasterDataChange\" workflow when one is defined, applied at once otherwise. "
                + "Tariff: FixedCharge, PerUnitRate, RatePerValuationCents, RatePerSquareMetre, MinimumCharge, IsActive. Customer: Name, PaymentTermsDays, SettlementDiscountPercent, OnHold, "
                + "InterestExempt, TaxRegistration, Email, IsActive. Supplier: Name, PaymentTermsDays, OnHold, TaxRegistration, TaxClearanceExpiry, IsActive. Item: Description, TaxCode, "
                + "StandardCost, CostOfSalesAccountCode, RevenueAccountCode, IsActive.",
            Fields = [Pick("Target", "Record", Enum.GetNames<Core.System.MasterDataTarget>()), Req("Key", "Code"), Req("Field", "Field"), Opt("Value", "New value (blank clears)")],
            Run = async (c, i) => Check(await c.Get<Application.Abstractions.System.IMasterDataChangeService>().RequestAsync(c.CompanyId, Enum.Parse<Core.System.MasterDataTarget>(i.Text("Target")),
                i.Text("Key"), i.Text("Field"), i.Optional("Value"), c.UserId).ConfigureAwait(true),
                r => r.Status == Core.System.MasterDataChangeStatus.Applied ? $"{r.Reference}: {r.Field} changed from '{r.OldValue}' to '{r.NewValue}'." : $"{r.Reference} is waiting for approval."),
            List = async c => Table(await c.Get<Application.Abstractions.System.IMasterDataChangeService>().ListAsync(c.CompanyId).ConfigureAwait(true),
                ("Reference", r => r.Reference), ("Record", r => $"{r.Target} {r.Key}"), ("Field", r => r.Field), ("Was", r => r.OldValue), ("Now", r => r.NewValue), ("Status", r => r.Status),
                ("Asked", r => r.RequestedOnUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Email approval links",
            Description = "The server address approvers reach from their email (e.g. https://erp.example.com). When set, each approver with an email address is also sent single-use "
                + "approve / reject links (valid 7 days, only while the document is still at their stage). Blank stops the links.",
            Fields = [Opt("Address", "Server address")],
            Run = async (c, i) =>
            {
                Core.System.Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                string? address = i.Optional("Address")?.Trim().TrimEnd('/');
                if (address is not null && !(Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
                {
                    throw new OperationException("Give the full address, starting http:// or https://.");
                }

                company.ApprovalLinkBaseUrl = address;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return address is null ? "Approval links are no longer emailed." : $"Approvers will be emailed links to {address}.";
            },
        };
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

    private static IEnumerable<Operation> WorkflowRules()
    {
        const string module = "Approvals";
        yield return new Operation
        {
            Module = module, Title = "Approve or reject several at once", ListTitle = "Waiting on me",
            Description = "Acts on every listed instance (ids separated by commas); each succeeds or fails on its own.",
            Fields = [Req("Instances", "Instance ids (1,2,3)"), Pick("Action", "Action", Enum.GetNames<Core.Workflow.WorkflowActionType>()), Opt("Comment", "Comment")],
            Run = async (c, i) =>
            {
                List<long> ids = [.. i.Text("Instances").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => long.Parse(x, System.Globalization.CultureInfo.InvariantCulture))];
                IReadOnlyList<BulkActionResult> results = await c.Get<IWorkflowService>().ActManyAsync(ids, c.UserId, Enum.Parse<Core.Workflow.WorkflowActionType>(i.Text("Action")), i.Optional("Comment"))
                    .ConfigureAwait(true);
                return string.Join(Environment.NewLine, results.Select(r => r.Succeeded ? $"{r.InstanceId}: {r.Status}" : $"{r.InstanceId}: not done — {r.Error}"));
            },
            List = WaitingOnMeAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set a stage's rules", ListTitle = "Workflow stages",
            Description = "A condition (e.g. Department = IT and Value > 5000; ops = != > >= < <= in), a role whose holders approve, and/or the requester's manager.",
            Fields = [Num("Definition", "Workflow definition id"), Num("Stage", "Stage sequence"), Opt("Condition", "Condition"), Opt("Role", "Approving role"),
                Pick("Dynamic", "Dynamic approver", Enum.GetNames<Core.Workflow.DynamicApprover>())],
            Run = async (c, i) => Check(await c.Get<IWorkflowService>().SetStageRulesAsync(c.CompanyId, (long)i.Number("Definition"), i.Whole("Stage"), i.Optional("Condition"), i.Optional("Role"),
                Enum.Parse<Core.Workflow.DynamicApprover>(i.Text("Dynamic"))).ConfigureAwait(true), "Stage rules saved."),
            List = async c => Table(await c.Db.WorkflowDefinitions.AsNoTracking().Include(d => d.Stages).Where(d => d.CompanyId == c.CompanyId).SelectMany(d => d.Stages.Select(s => new { d.Id, d.DocumentType, s.Sequence, s.Name, s.Condition, s.RequiredRoleName, s.DynamicApprover }))
                .ToListAsync().ConfigureAwait(true), ("Definition", x => x.Id), ("Document", x => x.DocumentType), ("Stage", x => x.Sequence), ("Name", x => x.Name), ("Condition", x => x.Condition),
                ("Role", x => x.RequiredRoleName), ("Dynamic", x => x.DynamicApprover)),
        };
        yield return new Operation
        {
            Module = module, Title = "Set a user's manager", ListTitle = "Waiting on me", Description = "The manager approves stages that ask for the requester's manager.",
            Fields = [Req("User", "User name"), Opt("Manager", "Manager's user name (blank: none)")],
            Run = async (c, i) => Check(await c.Get<One4All.Application.Abstractions.Security.IUserAdminService>().SetManagerAsync(await UserAsync(c, i.Text("User")).ConfigureAwait(true),
                await OptionalUserAsync(c, i.Optional("Manager")).ConfigureAwait(true)).ConfigureAwait(true), "Manager set."),
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
