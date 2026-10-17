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
            Description = "Changes a tariff's rates or a customer's, supplier's or item's field - held for the \"MasterDataChange\" workflow when one is defined, applied at once otherwise. "
                + "Tariff: FixedCharge, PerUnitRate, RatePerValuationCents, RatePerSquareMetre, MinimumCharge, IsActive. Customer: Name, PaymentTermsDays, SettlementDiscountPercent, SettlementDiscountDays, OnHold, "
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
                return string.Join(Environment.NewLine, results.Select(r => r.Succeeded ? $"{r.InstanceId}: {r.Status}" : $"{r.InstanceId}: not done - {r.Error}"));
            },
            List = WaitingOnMeAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Define a workflow", ListTitle = "Workflow definitions",
            Description = "The approval route for a document type (brief §7): stages in order, each with its quorum, the value from which it applies, a condition on the document "
                + "(e.g. Department = IT and Value > 5000), an approving role, named approvers, the requester's manager, and escalation after so many hours to someone. "
                + "Stages are written one per line as sequence | name | quorum | from value | condition | role | approvers (comma-separated user names) | Manager or None | escalate hours | escalate to | group. "
                + "Saving replaces the active definition for that document type (documents already in flight finish on the old one). Switch off/on by id with the Enable step.",
            Fields = [Pick("Step", "Step", ["Define", "Enable", "Disable"]), Opt("DocumentType", "Document type (" + string.Join(", ", typeof(WorkflowDocumentTypes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)) + ", or your own)"), Opt("Name", "Workflow name"),
                Opt("Stages", "Stages (one per line; see above)"), OptNum("Definition", "Definition id (enable / disable)")],
            Run = async (c, i) =>
            {
                IWorkflowService workflow = c.Get<IWorkflowService>();
                if (i.Text("Step") != "Define")
                {
                    long id = (long)(i.OptionalNumber("Definition") ?? throw new OperationException("Give the definition id (see the list)."));
                    return Check(await workflow.SetDefinitionActiveAsync(c.CompanyId, id, i.Text("Step") == "Enable").ConfigureAwait(true), i.Text("Step") == "Enable" ? "Workflow enabled." : "Workflow disabled.");
                }

                List<WorkflowStageInput> stages = [];
                foreach (string line in (i.Optional("Stages") ?? string.Empty).Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] p = [.. line.Split('|').Select(x => x.Trim())];
                    string At(int index) => index < p.Length ? p[index] : string.Empty;
                    if (p.Length < 2 || !int.TryParse(At(0), out int sequence))
                    {
                        throw new OperationException($"Stage line \"{line}\" needs at least sequence | name.");
                    }

                    stages.Add(new WorkflowStageInput(sequence, At(1), At(2).Length == 0 ? 1 : int.Parse(At(2), System.Globalization.CultureInfo.InvariantCulture),
                        At(3).Length == 0 ? null : decimal.Parse(At(3), System.Globalization.CultureInfo.InvariantCulture), At(4).Length == 0 ? null : At(4), At(5).Length == 0 ? null : At(5),
                        At(6).Length == 0 ? null : [.. At(6).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                        At(7).Equals("Manager", StringComparison.OrdinalIgnoreCase) || At(7).Equals("RequesterManager", StringComparison.OrdinalIgnoreCase) ? Core.Workflow.DynamicApprover.RequesterManager : Core.Workflow.DynamicApprover.None,
                        At(8).Length == 0 ? null : int.Parse(At(8), System.Globalization.CultureInfo.InvariantCulture), At(9).Length == 0 ? null : At(9), At(10).Length == 0 ? null : At(10)));
                }

                long created = Check(await workflow.DefineAsync(c.CompanyId, new WorkflowDefinitionInput(i.Optional("DocumentType") ?? string.Empty, i.Optional("Name") ?? string.Empty, stages)).ConfigureAwait(true));
                return $"Workflow {created} defined for {i.Optional("DocumentType")} with {stages.Count} stage(s); it routes new documents of that type from now.";
            },
            List = async c => Table(await c.Db.WorkflowDefinitions.AsNoTracking().Include(d => d.Stages).Where(d => d.CompanyId == c.CompanyId).OrderBy(d => d.DocumentType).ThenByDescending(d => d.Id)
                    .ToListAsync().ConfigureAwait(true),
                ("Id", d => d.Id), ("Document", d => d.DocumentType), ("Name", d => d.Name), ("Active", d => d.IsActive ? "yes" : "no"),
                ("Stages", d => string.Join(" → ", d.Stages.Select(s => $"{s.Sequence} {s.Name} (×{s.Quorum}{(s.MinValue is decimal m ? $", from {m:N0}" : string.Empty)}"
                    + $"{(s.RequiredRoleName is null ? string.Empty : $", role {s.RequiredRoleName}")}{(s.EligibleUserIds.Count == 0 ? string.Empty : $", {s.EligibleUserIds.Count} user(s)")}"
                    + $"{(s.DynamicApprover == Core.Workflow.DynamicApprover.None ? string.Empty : ", manager")}{(s.EscalateAfterHours is int h ? $", escalates after {h} h" : string.Empty)})")))),
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
        yield return new Operation
        {
            Module = module, Title = "Segregation-of-duties rules", ListTitle = "Rules in force",
            Description = "The conflicts no one person may hold (create a supplier and approve its payment, say). Add a rule, re-tune a built-in one by its code, or switch it "
                + "off (inactive); the control reports and role assignments check the rules in force. Operations by name: Create, Edit, Approve, Post…",
            Fields = [Req("Code", "Rule code"), Req("Name", "What the conflict is"), Req("PermissionA", "Permission (e.g. Purchasing.Supplier)"), Req("OperationsA", "Its operations (Create, Edit…)"),
                Req("PermissionB", "Conflicting permission"), Req("OperationsB", "Its operations"), Pick("Severity", "Severity", Enum.GetNames<Core.Security.SodSeverity>()), Tick("Active", "In force", true)],
            Run = async (c, i) =>
            {
                static Core.Security.OperationFlags Flags(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Aggregate(Core.Security.OperationFlags.None, (all, name) => all | (Enum.TryParse(name, true, out Core.Security.OperationFlags flag) ? flag
                        : throw new OperationException($"No operation called {name}.")));
                Core.Security.SodRule rule = new(i.Text("Code"), i.Text("Name"), i.Text("PermissionA"), Flags(i.Text("OperationsA")), i.Text("PermissionB"), Flags(i.Text("OperationsB")),
                    Enum.Parse<Core.Security.SodSeverity>(i.Text("Severity")));
                return Check(await c.Get<One4All.Application.Abstractions.Security.IPermissionService>().SaveSodRuleAsync(rule, i.Flag("Active")).ConfigureAwait(true),
                    i.Flag("Active") ? "Rule saved." : "Rule switched off.");
            },
            List = async c => Table(await c.Get<One4All.Application.Abstractions.Security.IPermissionService>().SodRulesAsync().ConfigureAwait(true),
                ("Code", r => r.Code), ("Rule", r => r.Name), ("Permission", r => $"{r.PermissionA} {r.OperationA}"), ("Conflicts with", r => $"{r.PermissionB} {r.OperationB}"),
                ("Severity", r => r.Severity)),
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
