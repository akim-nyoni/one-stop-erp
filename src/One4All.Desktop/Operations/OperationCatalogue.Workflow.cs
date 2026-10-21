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
                + "InterestExempt, TaxRegistration, Email, IsActive. Supplier: Name, PaymentTermsDays, SettlementDiscountPercent, SettlementDiscountDays, OnHold, TaxRegistration, TaxClearanceExpiry, IsActive. Item: Description, TaxCode, "
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
            Module = module, Title = "Delegate my approvals", ListTitle = "Approval delegations", KeyColumn = "Id",
            Description = "While you are away, the delegate may approve anything you could, recorded as on your behalf. You delegate only your own approvals "
                + "(a system administrator may record one for an approver who is away). With a delegation id (Edit fills it in) "
                + "the delegate, dates and reason of that delegation change (the approver stays); Delete withdraws a delegation early (kept in history).",
            Fields = [Opt("From", "Approver who is away (blank: me)"), Req("To", "Delegate (user name)"), Today("Starts", "Starts"), Today("Ends", "Ends"), Req("Reason", "Reason", "Leave"),
                OptNum("Id", "Delegation id (change)")],
            Run = async (c, i) =>
            {
                long from = await OptionalUserAsync(c, i.Optional("From")).ConfigureAwait(true) ?? c.UserId, to = await UserAsync(c, i.Text("To")).ConfigureAwait(true);
                if (i.OptionalNumber("Id") is not decimal given)
                {
                    return Check(await c.Get<IWorkflowService>().DelegateAsync(c.CompanyId, from, to, i.Date("Starts"), i.Date("Ends"), i.Text("Reason"), c.UserId).ConfigureAwait(true),
                        id => $"Delegation {id} recorded.");
                }

                await OwnDelegationAsync(c, from).ConfigureAwait(true);

                long delegationId = (long)given;
                Core.Workflow.ApprovalDelegation delegation = await c.Db.ApprovalDelegations.FirstOrDefaultAsync(d => d.CompanyId == c.CompanyId && d.Id == delegationId).ConfigureAwait(true)
                    ?? throw new OperationException($"No delegation {delegationId}.");
                if (delegation.Withdrawn)
                {
                    throw new OperationException($"Delegation {delegationId} was withdrawn; record a new one instead.");
                }

                if (delegation.FromUserId != from)
                {
                    throw new OperationException($"Delegation {delegationId} is for another approver; the approver who is away cannot change.");
                }

                (DateOnly starts, DateOnly ends) = (i.Date("Starts"), i.Date("Ends"));
                if (from == to || ends < starts)
                {
                    throw new OperationException("A delegation is to someone else, for a period, with a reason.");
                }

                if (await c.Db.ApprovalDelegations.AnyAsync(d => d.CompanyId == c.CompanyId && d.Id != delegationId && d.FromUserId == from && !d.Withdrawn && d.StartsOn <= ends && d.EndsOn >= starts)
                    .ConfigureAwait(true))
                {
                    throw new OperationException("The approver already has another delegation covering some of those dates.");
                }

                delegation.Change(to, starts, ends, i.Text("Reason").Trim());
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Delegation {delegationId} changed.";
            },
            List = DelegationsAsync,
            Delete = async (c, key) =>
            {
                long id = long.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long parsed) ? parsed
                    : throw new OperationException($"\"{key}\" is not a delegation id.");
                Core.Workflow.ApprovalDelegation delegation = await c.Db.ApprovalDelegations.FirstOrDefaultAsync(d => d.CompanyId == c.CompanyId && d.Id == id).ConfigureAwait(true)
                    ?? throw new OperationException($"No delegation {id}.");
                if (delegation.Withdrawn)
                {
                    throw new OperationException($"Delegation {id} is already withdrawn.");
                }

                await OwnDelegationAsync(c, delegation.FromUserId).ConfigureAwait(true);

                delegation.Withdrawn = true;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Delegation {id} withdrawn (kept in history).";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Add an approver to a document", ListTitle = "Waiting on me",
            Description = "Adds someone to the current stage of one document's approval, alongside the stage's own approvers. Only an approver of that stage (or a system "
                + "administrator) can add one, and never the person who raised the document nor themselves.",
            Fields = [Num("Instance", "Workflow instance id"), Req("User", "Approver (user name)")],
            Run = async (c, i) => Check(await c.Get<IWorkflowService>().AddApproverAsync((long)i.Number("Instance"), await UserAsync(c, i.Text("User")).ConfigureAwait(true), c.UserId).ConfigureAwait(true),
                "Approver added."),
            List = WaitingOnMeAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Discuss a document", ListTitle = "Waiting on me",
            Description = "The comment thread of a document under approval: ask the requester a question, answer an approver, or add a fact, without approving or rejecting. "
                + "The requester, anyone who has acted on it and anyone who may approve its current stage can write; leave the comment blank to read the thread.",
            Fields = [Num("Instance", "Workflow instance id"), Opt("Comment", "Comment (blank: read the thread)")],
            Run = async (c, i) =>
            {
                IWorkflowService workflow = c.Get<IWorkflowService>();
                long instance = (long)i.Number("Instance");
                if (i.Optional("Comment") is string remark)
                {
                    _ = Check(await workflow.CommentAsync(instance, c.UserId, remark).ConfigureAwait(true), string.Empty);
                }

                IReadOnlyList<WorkflowThreadEntry> thread = Check(await workflow.ThreadAsync(instance).ConfigureAwait(true));
                return thread.Count == 0 ? "No comments or decisions yet."
                    : string.Join(Environment.NewLine, thread.Select(e => $"{e.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm} {e.UserName} - {e.Kind}{(e.Text is null ? string.Empty : ": " + e.Text)}"));
            },
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
                + "Stages are written one per line as sequence | name | quorum | from value | condition | role | approvers (comma-separated user names) | Manager or None | escalate hours | escalate to | group | parallel groups (comma-separated: one approval from each). "
                + "Saving replaces the active definition for that document type (documents already in flight finish on the old one). Switch off/on by id with the Enable step. "
                + "Edit fills the form from a definition so a changed route can be saved over it; Delete switches a definition off (kept in history).",
            KeyColumn = "Id",
            Delete = async (c, key) =>
            {
                long id = long.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long parsed) ? parsed
                    : throw new OperationException($"\"{key}\" is not a definition id.");
                return Check(await c.Get<IWorkflowService>().SetDefinitionActiveAsync(c.CompanyId, id, false).ConfigureAwait(true), $"Workflow {id} switched off (kept in history).");
            },
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
                        At(8).Length == 0 ? null : int.Parse(At(8), System.Globalization.CultureInfo.InvariantCulture), At(9).Length == 0 ? null : At(9), At(10).Length == 0 ? null : At(10),
                        At(11).Length == 0 ? null : At(11)));
                }

                long created = Check(await workflow.DefineAsync(c.CompanyId, new WorkflowDefinitionInput(i.Optional("DocumentType") ?? string.Empty, i.Optional("Name") ?? string.Empty, stages)).ConfigureAwait(true));
                return $"Workflow {created} defined for {i.Optional("DocumentType")} with {stages.Count} stage(s); it routes new documents of that type from now.";
            },
            List = async c =>
            {
                Dictionary<long, string> users = await c.Db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username).ConfigureAwait(true);
                string Line(Core.Workflow.WorkflowStage s) => string.Join(" | ", s.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Name,
                    s.Quorum.ToString(System.Globalization.CultureInfo.InvariantCulture), s.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, s.Condition ?? string.Empty,
                    s.RequiredRoleName ?? string.Empty, string.Join(",", s.EligibleUserIds.Select(u => users.GetValueOrDefault(u, u.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
                    s.DynamicApprover == Core.Workflow.DynamicApprover.None ? "None" : "Manager", s.EscalateAfterHours?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    s.EscalateToUserId is long e ? users.GetValueOrDefault(e, string.Empty) : string.Empty, s.RequiredGroupName ?? string.Empty, s.ParallelGroups ?? string.Empty);
                return Table(await c.Db.WorkflowDefinitions.AsNoTracking().Include(d => d.Stages).Where(d => d.CompanyId == c.CompanyId).OrderBy(d => d.DocumentType).ThenByDescending(d => d.Id)
                        .ToListAsync().ConfigureAwait(true),
                    ("Step", _ => "Define"), ("Id", d => d.Id), ("Document type", d => d.DocumentType), ("Name", d => d.Name), ("Active", d => d.IsActive ? "Yes" : "No"),
                    ("Stages", d => string.Join("; ", d.Stages.OrderBy(s => s.Sequence).Select(Line))));
            },
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
                ("Code", r => r.Code), ("Name", r => r.Name), ("Permission A", r => r.PermissionA), ("Operations A", r => r.OperationA), ("Permission B", r => r.PermissionB),
                ("Operations B", r => r.OperationB), ("Severity", r => r.Severity)),
            Delete = async (c, code) =>
            {
                One4All.Application.Abstractions.Security.IPermissionService permissions = c.Get<One4All.Application.Abstractions.Security.IPermissionService>();
                Core.Security.SodRule rule = (await permissions.SodRulesAsync().ConfigureAwait(true)).FirstOrDefault(r => string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new OperationException($"No rule {code} in force.");
                return Check(await permissions.SaveSodRuleAsync(rule, false).ConfigureAwait(true), $"Rule {rule.Code} switched off (kept in history; save it as in force to bring it back).");
            },
        };
    }

    private static async Task<DataTable> WaitingOnMeAsync(OperationContext c)
        => Table(await c.Get<IWorkflowService>().PendingForUserAsync(c.CompanyId, c.UserId).ConfigureAwait(true),
            ("Instance", p => p.InstanceId), ("Document", p => $"{p.DocumentType} {p.DocumentReference}"), ("Value", p => M(p.Value)), ("Stage", p => p.StageName),
            ("Approvals", p => $"{p.ApprovalsSoFar}/{p.Quorum}"), ("On behalf of", p => p.OnBehalfOfUserId));

    // Only the approver who is away changes or withdraws their delegation; a system administrator may for anyone.
    private static async Task OwnDelegationAsync(OperationContext c, long fromUserId)
    {
        if (fromUserId != c.UserId && !await c.Db.Users.AnyAsync(u => u.Id == c.UserId && u.IsActive && u.IsSystemAdministrator).ConfigureAwait(true))
        {
            throw new OperationException("You can only delegate your own approvals.");
        }
    }

    private static async Task<DataTable> DelegationsAsync(OperationContext c)
    {
        Dictionary<long, string> users = await c.Db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username).ConfigureAwait(true);
        return Table(await c.Db.ApprovalDelegations.AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.StartsOn).ToListAsync().ConfigureAwait(true),
            ("Id", d => d.Id), ("From", d => users.GetValueOrDefault(d.FromUserId)), ("To", d => users.GetValueOrDefault(d.ToUserId)), ("Starts", d => D(d.StartsOn)), ("Ends", d => D(d.EndsOn)), ("Reason", d => d.Reason),
            ("Withdrawn", d => d.Withdrawn));
    }
}
