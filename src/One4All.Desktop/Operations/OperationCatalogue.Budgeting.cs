using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Budget preparation: submission, review, negotiation and approval.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Budgeting()
    {
        const string module = "Budgeting";
        yield return new Operation
        {
            Module = module, Title = "Create or copy a budget", ListTitle = "Budgets",
            Description = "Create a budget for a fiscal year from lines ACCOUNT:period:amount[:cost centre]; or copy an existing budget as the start of a new version "
                + "(a revision, a supplementary budget or a reforecast), optionally uplifted by a percentage — the copy records which budget it came from.",
            Fields = [Pick("Step", "Step", ["Create", "Copy"]), Opt("Year", "Fiscal year code (create)"), Opt("Label", "Label"), Pick("Type", "Budget type", Enum.GetNames<Core.Budgeting.BudgetType>()),
                Opt("Department", "Department (blank: company-wide)"), Opt("Lines", "Lines ACCOUNT:period:amount[:cost centre]; … (create)"), OptNum("From", "Budget id to copy"),
                OptNum("Uplift", "Uplift % on the copy"), Opt("Reason", "Why (copy)")],
            Run = async (c, i) =>
            {
                IBudgetingService budgeting = c.Get<IBudgetingService>();
                Core.Budgeting.BudgetType type = Enum.Parse<Core.Budgeting.BudgetType>(i.Text("Type"));
                if (i.Text("Step") == "Create")
                {
                    string code = i.Optional("Year") ?? throw new OperationException("Give the fiscal year.");
                    long year = await FindAsync(c.Db.FiscalYears, y => y.CompanyId == c.CompanyId && y.Code == code, y => y.Id, "fiscal year", code).ConfigureAwait(true);
                    List<NewBudgetLine> lines = [];
                    foreach (string part in (i.Optional("Lines") ?? throw new OperationException("Give the budget lines.")).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        if (bits.Length < 3 || !int.TryParse(bits[1], out int period)
                            || !decimal.TryParse(bits[2], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                        {
                            throw new OperationException($"'{part}' is not ACCOUNT:period:amount[:cost centre].");
                        }

                        lines.Add(new NewBudgetLine(bits[0], period, amount, bits.Length > 3 && bits[3].Length > 0 ? bits[3] : null));
                    }

                    long created = Check(await budgeting.CreateBudgetAsync(c.CompanyId, year, i.Optional("Label") ?? code, type, i.Optional("Department"), lines).ConfigureAwait(true));
                    return $"Budget {created} created with {lines.Count} line(s), total {M(lines.Sum(l => l.Amount))}.";
                }

                long sourceId = (long)(i.OptionalNumber("From") ?? throw new OperationException("Give the budget to copy."));
                Core.Budgeting.Budget source = await c.Db.Set<Core.Budgeting.Budget>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.CompanyId == c.CompanyId && b.Id == sourceId)
                    .ConfigureAwait(true) ?? throw new OperationException($"No budget {sourceId}.");
                decimal factor = 1m + ((i.OptionalNumber("Uplift") ?? 0m) / 100m);
                List<NewBudgetLine> copied = [.. source.Lines.GroupBy(l => (l.AccountCode, l.CostCentre, l.Period, l.Project, l.Fund, l.Branch))
                    .Select(g => new NewBudgetLine(g.Key.AccountCode, g.Key.Period, decimal.Round(g.Sum(l => l.Amount) * factor, 2, MidpointRounding.AwayFromZero), g.Key.CostCentre,
                        Project: g.Key.Project, Fund: g.Key.Fund, Branch: g.Key.Branch))
                    .Where(l => l.Amount != 0m)];
                long copy = Check(await budgeting.CreateBudgetAsync(c.CompanyId, source.FiscalYearId, i.Optional("Label") ?? source.Label, type, i.Optional("Department") ?? source.Department, copied)
                    .ConfigureAwait(true));
                Core.Budgeting.Budget made = await c.Db.Set<Core.Budgeting.Budget>().FirstAsync(b => b.Id == copy).ConfigureAwait(true);
                made.Reason = $"Copied from budget {source.Id} ({source.Label} {source.Type} v{source.VersionNumber})" + (i.Optional("Reason") is string why ? $": {why}" : ".");
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Budget {copy} (v{made.VersionNumber}) created from budget {source.Id}: {copied.Count} line(s), total {M(copied.Sum(l => l.Amount))}.";
            },
            List = async c => Table(await (
                    from b in c.Db.Set<Core.Budgeting.Budget>().AsNoTracking()
                    join y in c.Db.FiscalYears.AsNoTracking() on b.FiscalYearId equals y.Id
                    where b.CompanyId == c.CompanyId
                    orderby y.StartDate descending, b.Id descending
                    select new { b.Id, Year = y.Code, b.Label, b.Type, b.Department, b.VersionNumber, b.Status, b.IsActive, b.Reason }).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Id", b => b.Id), ("Year", b => b.Year), ("Label", b => b.Label), ("Type", b => b.Type.ToString()), ("Department", b => b.Department ?? "-"), ("Version", b => b.VersionNumber),
                ("Status", b => b.Status.ToString()), ("Active", b => b.IsActive ? "yes" : string.Empty), ("Note", b => b.Reason ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Carry unspent capital forward", ListTitle = "Capital budgets",
            Description = "Year-end: Propose lists each capital vote's approved budget, what was spent and what may still be carried; Approve carries the given amounts (VOTE:amount; …) "
                + "into a later year's capital budget under the approval reference — a separate act, recorded for audit.",
            Fields = [Pick("Step", "Step", ["Propose", "Approve"]), Req("Year", "From fiscal year (code)"), Opt("Budget", "Into capital budget (label, approve)"),
                Opt("Amounts", "VOTE:amount; … (approve)"), Opt("Reference", "Approval reference"), Today("AsAt", "As at")],
            Run = async (c, i) =>
            {
                ICapitalCarryForwardService carry = c.Get<ICapitalCarryForwardService>();
                long year = await FindAsync(c.Db.FiscalYears, y => y.CompanyId == c.CompanyId && y.Code == i.Text("Year"), y => y.Id, "fiscal year", i.Text("Year")).ConfigureAwait(true);
                if (i.Text("Step") == "Propose")
                {
                    IReadOnlyList<CapitalUnspent> votes = Check(await carry.ProposeAsync(c.CompanyId, year, i.Date("AsAt")).ConfigureAwait(true));
                    return votes.Count == 0 ? "No approved capital budget for that year." : string.Join(Environment.NewLine, votes.Select(v =>
                        $"{v.AccountCode,-10} budget {M(v.Budget),14}  spent {M(v.Spent),14}  carried {M(v.AlreadyCarried),12}  may carry {M(v.Carryable),14}"));
                }

                string label = i.Optional("Budget") ?? throw new OperationException("Name the capital budget to carry into.");
                long target = await FindAsync(c.Db.Set<Budget>(), b => b.CompanyId == c.CompanyId && b.Label == label && b.Type == BudgetType.Capital, b => b.Id, "capital budget", label).ConfigureAwait(true);
                List<CarryForwardAmount> amounts = [];
                foreach (string part in (i.Optional("Amounts") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    amounts.Add(bits.Length == 2 && decimal.TryParse(bits[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount)
                        ? new CarryForwardAmount(bits[0], amount) : throw new OperationException($"Write each as VOTE:amount (\"{part}\")."));
                }

                decimal total = Check(await carry.ApproveAsync(c.CompanyId, year, target, amounts, i.Optional("Reference") ?? string.Empty, c.UserId, i.Date("AsAt")).ConfigureAwait(true));
                return $"{M(total)} of unspent capital carried into {label}.";
            },
            List = async c => Table(await c.Db.Set<Budget>().AsNoTracking().Where(b => b.CompanyId == c.CompanyId && b.Type == BudgetType.Capital).OrderByDescending(b => b.Id).Page(c, 100)
                    .ToListAsync().ConfigureAwait(true),
                ("Budget", b => b.Label), ("Version", b => b.VersionNumber), ("Status", b => b.Status)),
        };
        string[] steps = ["Submit", "Start review", "Return", "Recommend", "Approve", "Accept proposals"];
        yield return new Operation
        {
            Module = module, Title = "Preparation deadlines and budget alerts", ListTitle = "Preparation board",
            Description = "Set a department's submission deadline; remind budget holders whose submission is due within the days given (or overdue); set where utilisation alerts go "
                + "and their thresholds; send the alerts now (each vote and threshold once). The scheduled task budget.notices does both daily.",
            Fields = [Pick("Step", "Step", ["Set deadline", "Send reminders", "Alert settings", "Send alerts"]), Opt("Department", "Department"), new("Deadline", "Deadline", FieldKind.Date, Required: false),
                Num("Year", "Fiscal year id (board / reminders)", "0"), Num("Days", "Remind this many days before", "7"), Opt("Email", "Alert email"), Opt("Thresholds", "Thresholds % (e.g. 75, 90, 100)"), Today()],
            Run = async (c, i) =>
            {
                IBudgetNotificationService notices = c.Get<IBudgetNotificationService>();
                switch (i.Text("Step"))
                {
                    case "Set deadline":
                        return Check(await notices.SetDeadlineAsync(c.CompanyId, i.Optional("Department") ?? string.Empty, i.OptionalDate("Deadline")).ConfigureAwait(true), "Deadline set.");
                    case "Send reminders":
                        return Check(await notices.RemindAsync(c.CompanyId, (long)i.Number("Year"), i.Date("Date"), i.Whole("Days")).ConfigureAwait(true),
                            n => $"{n.Messages} reminder(s) queued for {n.Items} department(s) due.");
                    case "Alert settings":
                        Core.System.Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                        company.BudgetAlertEmail = i.Optional("Email");
                        company.BudgetAlertThresholds = i.Optional("Thresholds");
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return "Alert settings saved.";
                    default:
                        return Check(await notices.AlertAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true), n => n.Items == 0 ? "No new thresholds crossed." : $"{n.Items} vote(s) alerted.");
                }
            },
            List = async c =>
            {
                long year = await c.Db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == c.CompanyId).OrderByDescending(y => y.StartDate).Select(y => y.Id).FirstOrDefaultAsync().ConfigureAwait(true);
                return Table(await c.Get<IBudgetNotificationService>().BoardAsync(c.CompanyId, year, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                    ("Department", r => r.Department), ("Holder", r => r.BudgetHolder ?? "—"), ("Deadline", r => r.Deadline is DateOnly d ? D(d) : "—"), ("Status", r => r.Status ?? "not started"),
                    ("Days left", r => r.DaysLeft?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "—"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Move a budget through preparation", ListTitle = "Budgets in preparation",
            Description = "Submit (department) → start review (Finance) → return with reasons or recommend → approve with the board/council resolution (not by the submitter). "
                + "A returned budget can take Finance's proposals and be resubmitted as a new round.",
            Fields = [Num("Budget", "Budget id"), Pick("Step", "Step", steps), Opt("Note", "Comment / resolution reference")],
            Run = async (c, i) =>
            {
                IBudgetPreparationService prep = c.Get<IBudgetPreparationService>();
                long id = (long)i.Number("Budget");
                string? note = i.Optional("Note");
                return i.Text("Step") switch
                {
                    "Submit" => Check(await prep.SubmitAsync(c.CompanyId, id, c.UserId, note).ConfigureAwait(true), "Submitted."),
                    "Start review" => Check(await prep.StartReviewAsync(c.CompanyId, id, c.UserId).ConfigureAwait(true), "Under review."),
                    "Return" => Check(await prep.ReturnAsync(c.CompanyId, id, c.UserId, note ?? string.Empty).ConfigureAwait(true), "Returned to the department."),
                    "Recommend" => Check(await prep.RecommendAsync(c.CompanyId, id, c.UserId, note).ConfigureAwait(true), "Recommended."),
                    "Approve" => Check(await prep.ApproveAsync(c.CompanyId, id, c.UserId, note ?? string.Empty).ConfigureAwait(true), "Approved and sealed."),
                    _ => $"{Check(await prep.AcceptProposalsAsync(c.CompanyId, id, c.UserId).ConfigureAwait(true))} line(s) took Finance's figures.",
                };
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Propose or revise a figure", ListTitle = "Budgets in preparation",
            Description = "Finance proposes a figure on a budget under review (kept beside the department's request); the department revises a draft or returned one.",
            Fields = [Num("Budget", "Budget id"), Req("Account", "Account"), Num("Period", "Period", "1"), Num("Amount", "Amount"), Opt("CostCentre", "Cost centre"), Opt("Comment", "Why"),
                Tick("Propose", "Finance proposal (untick to revise)")],
            Run = async (c, i) =>
            {
                ProposedFigure figure = new(i.Text("Account"), i.Whole("Period"), i.Number("Amount"), i.Optional("CostCentre"), i.Optional("Comment"));
                IBudgetPreparationService prep = c.Get<IBudgetPreparationService>();
                return i.Flag("Propose")
                    ? Check(await prep.ProposeAsync(c.CompanyId, (long)i.Number("Budget"), c.UserId, [figure]).ConfigureAwait(true), "Proposal recorded.")
                    : Check(await prep.ReviseAsync(c.CompanyId, (long)i.Number("Budget"), c.UserId, figure).ConfigureAwait(true), "Figure revised.");
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Show a budget's negotiation trail", ListTitle = "Budgets in preparation",
            Description = "Every step and every proposal, the department's figure beside Finance's, round by round.",
            Fields = [Num("Budget", "Budget id")],
            Run = async (c, i) =>
            {
                BudgetTrail trail = Check(await c.Get<IBudgetPreparationService>().TrailAsync(c.CompanyId, (long)i.Number("Budget")).ConfigureAwait(true));
                IEnumerable<string> steps = trail.Events.Select(e => $"Round {e.Round}: {e.Action} by user {e.UserId} on {e.OccurredOnUtc:yyyy-MM-dd HH:mm}{(e.Comment is null ? string.Empty : $" — {e.Comment}")}");
                IEnumerable<string> figures = trail.Proposals.Select(p => $"Round {p.Round}: {p.AccountCode} p{p.Period} requested {M(p.Requested)}, proposed {M(p.Proposed)}{(p.Comment is null ? string.Empty : $" ({p.Comment})")}");
                return string.Join(Environment.NewLine, [$"{trail.Status}, round {trail.Round}{(trail.ResolutionReference is null ? string.Empty : $", resolution {trail.ResolutionReference}")}", .. steps, .. figures]);
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Budget-holder statements", ListTitle = "Budgets in preparation",
            Description = "Each department's active budget against actuals to the period of the date — budget and actual to date, the variance and the share of the year's "
                + "budget used, with votes past the utilisation thresholds called out. Preview shows one department's; Send emails every budget holder with an address "
                + "(the scheduled budget notices send them on the first of each month for the month just ended).",
            Fields = [Pick("Step", "Step", ["Preview", "Send"]), Opt("Department", "Department (preview)"), Today("Date", "As at")],
            Run = async (c, i) =>
            {
                IBudgetNotificationService notices = c.Get<IBudgetNotificationService>();
                if (i.Text("Step") == "Preview")
                {
                    return Check(await notices.HolderStatementAsync(c.CompanyId, i.Optional("Department") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                }

                BudgetNotices sent = Check(await notices.HolderStatementsAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                return $"{sent.Messages} statement(s) queued for {sent.Items} department(s) with a budget holder address.";
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set a budget's currency and rate", ListTitle = "Budgets in preparation",
            Description = "Prepares a budget in a currency at a budget rate held on its header: the lines are planned in that currency and their base equivalents follow the rate, "
                + "so a new rate re-states them, and the FX variance is measured against it. Blank currency (or the base currency) returns the budget to base.",
            Fields = [Num("Budget", "Budget id"), CurrencyPick("Currency", "Budget currency"), OptNum("Rate", "Budget rate (base per unit)")],
            Run = async (c, i) =>
            {
                Core.Accounting.CurrencyCode? currency = i.Optional("Currency") is string code ? Core.Accounting.CurrencyCode.Parse(code) : null;
                return Check(await c.Get<IBudgetingService>().SetBudgetCurrencyAsync(c.CompanyId, (long)i.Number("Budget"), currency, i.OptionalNumber("Rate")).ConfigureAwait(true),
                    currency is null ? "The budget is in the base currency." : $"The budget is in {currency} at {i.OptionalNumber("Rate"):0.######}; base equivalents re-stated.");
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set an assumption", ListTitle = "Assumptions",
            Description = "A named assumption for the year (inflation, fee, headcount cost…). Driver-based lines are quantity × its value; untick Apply to preview what would change.",
            Fields = [Num("Year", "Fiscal year id"), Req("Code", "Assumption code"), Num("Value", "Value"), Opt("Name", "Name"), Opt("Unit", "Unit"), Tick("Apply", "Apply")],
            Run = async (c, i) =>
            {
                IReadOnlyList<DriverImpact> impacts = Check(await c.Get<IBudgetPlanningService>().SetDriverAsync(c.CompanyId, (long)i.Number("Year"), i.Text("Code"), i.Number("Value"), i.Flag("Apply"),
                    i.Optional("Name"), i.Optional("Unit")).ConfigureAwait(true));
                string head = $"{impacts.Count} line(s) {(i.Flag("Apply") ? "re-computed" : "would change")}, net {M(impacts.Sum(x => x.After - x.Before))}.";
                return string.Join(Environment.NewLine, [head, .. impacts.Take(30).Select(x => $"Budget {x.BudgetId} {x.AccountCode} p{x.Period}: {M(x.Before)} → {M(x.After)}")]);
            },
            List = async c => Table(await c.Db.BudgetDrivers.AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderBy(d => d.FiscalYearId).ThenBy(d => d.Code).ToListAsync().ConfigureAwait(true),
                ("Year", d => d.FiscalYearId), ("Code", d => d.Code), ("Name", d => d.Name), ("Unit", d => d.Unit), ("Value", d => Q(d.Value))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a driver-based line", ListTitle = "Budgets in preparation", Description = "A line of quantity × an assumption (e.g. 40 learners × FEE), re-computed when the assumption changes.",
            Fields = [Num("Budget", "Budget id"), Req("Account", "Account"), Num("Period", "Period", "1"), Req("Driver", "Assumption code"), Num("Quantity", "Quantity"), Opt("CostCentre", "Cost centre")],
            Run = async (c, i) => $"Line set to {M(Check(await c.Get<IBudgetPlanningService>().SetDriverLineAsync(c.CompanyId, (long)i.Number("Budget"), i.Text("Account"), i.Whole("Period"), i.Text("Driver"),
                i.Number("Quantity"), i.Optional("CostCentre")).ConfigureAwait(true)))}.",
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Spread an annual figure", ListTitle = "Budgets in preparation",
            Description = "Spreads an account's annual amount over the year: even, seasonal (last year's actuals), S-curve, working days, or custom weights (w1;w2;… one per period).",
            Fields = [Num("Budget", "Budget id"), Req("Account", "Account"), Num("Annual", "Annual amount"), Pick("Profile", "Profile", Enum.GetNames<SpreadProfile>()), Opt("Weights", "Custom weights"),
                Opt("CostCentre", "Cost centre")],
            Run = async (c, i) =>
            {
                List<decimal>? weights = i.Optional("Weights") is string w
                    ? [.. w.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture))]
                    : null;
                IReadOnlyList<decimal> parts = Check(await c.Get<IBudgetPlanningService>().SpreadAsync(c.CompanyId, (long)i.Number("Budget"), i.Text("Account"), i.Number("Annual"),
                    Enum.Parse<SpreadProfile>(i.Text("Profile")), weights, i.Optional("CostCentre")).ConfigureAwait(true));
                return "Spread: " + string.Join(", ", parts.Select(M));
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Build the staff budget from the establishment", ListTitle = "Budgets in preparation",
            Description = "Approved posts × annual cost per post, plus the increment, less the vacancy factor, spread evenly; employer contributions to their own account. Re-run after a salary change.",
            Fields = [Num("Budget", "Budget id"), Req("Salaries", "Salaries account", "6000"), Num("Increment", "Increment %", "0"), Num("Vacancy", "Vacancy factor %", "0"),
                Num("Contribution", "Employer contributions %", "0"), Opt("ContributionAccount", "Contributions account")],
            Run = async (c, i) =>
            {
                StaffBudget staff = Check(await c.Get<IBudgetPlanningService>().BuildStaffBudgetAsync(c.CompanyId, (long)i.Number("Budget"), i.Text("Salaries"), i.Number("Increment"), i.Number("Vacancy"),
                    i.Number("Contribution"), i.Optional("ContributionAccount")).ConfigureAwait(true));
                return $"{staff.Posts} post(s): salaries {M(staff.Salaries)}, contributions {M(staff.Contributions)}.";
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Export a budget to Excel", ListTitle = "Budgets in preparation",
            Description = "Writes the budget as an .xlsx workbook (a row per account and cost centre, P1 to P12 and a total) into the folder given, for editing offline. Keep the first two rows as they are.",
            Fields = [Num("Budget", "Budget id"), Tick("All", "Add a zero row for every budget account"),
                Req("Folder", "Folder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))],
            Run = async (c, i) =>
            {
                (byte[] workbook, string file) = Check(await c.Get<IBudgetWorkbookService>().ExportAsync(c.CompanyId, (long)i.Number("Budget"), i.Flag("All")).ConfigureAwait(true));
                string path = System.IO.Path.Combine(i.Text("Folder"), file);
                await System.IO.File.WriteAllBytesAsync(path, workbook).ConfigureAwait(true);
                return $"Saved {path}.";
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Import a budget workbook", ListTitle = "Budgets in preparation",
            Description = "Reads an edited budget workbook back: every row is checked and the differences listed. Tick Apply to write them (draft or returned budgets only; nothing is written while any row is wrong). Rows deleted from the workbook leave those figures unchanged.",
            Fields = [Num("Budget", "Budget id"), Req("File", "Workbook (.xlsx) path"), Tick("Apply", "Apply the changes")],
            Run = async (c, i) =>
            {
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(i.Text("File")).ConfigureAwait(true);
                BudgetWorkbookImport import = Check(await c.Get<IBudgetWorkbookService>().ImportAsync(c.CompanyId, (long)i.Number("Budget"), bytes, c.UserId, i.Flag("Apply")).ConfigureAwait(true));
                string head = $"{import.Changes.Count} figure(s) differ; total {M(import.AnnualTotalBefore)} → {M(import.AnnualTotalAfter)}. "
                    + (import.Applied ? "Applied." : import.Problems.Count > 0 ? $"{import.Problems.Count} problem(s) — nothing written:" : "Preview only (tick Apply to write).");
                IEnumerable<string> detail = import.Problems.Count > 0
                    ? import.Problems.Select(p => $"  row {p.Row}: {p.Message}")
                    : import.Changes.Take(40).Select(ch => $"  {ch.AccountCode}{(ch.CostCentre is null ? string.Empty : $"/{ch.CostCentre}")} P{ch.Period}: {M(ch.Before)} → {M(ch.After)}");
                return string.Join(Environment.NewLine, detail.Prepend(head));
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Re-phase an approved budget", ListTitle = "Re-phasing requests",
            Description = "Moves an amount between periods of an approved budget (the annual total does not change). Ask, then someone else approves or rejects (give the request id).",
            Fields = [Opt("Request", "Request id to decide (blank: new request)"), Tick("Approve", "Approve (untick to reject)"), OptNum("Budget", "Budget id"), Opt("Account", "Account"),
                OptNum("From", "From period"), OptNum("To", "To period"), OptNum("Amount", "Amount"), Opt("Reason", "Reason")],
            Run = async (c, i) =>
            {
                IBudgetPlanningService planning = c.Get<IBudgetPlanningService>();
                if (i.Optional("Request") is string request)
                {
                    return Check(await planning.DecideRephaseAsync(c.CompanyId, long.Parse(request, System.Globalization.CultureInfo.InvariantCulture), i.Flag("Approve"), c.UserId).ConfigureAwait(true),
                        i.Flag("Approve") ? "Re-phasing approved and applied." : "Re-phasing rejected.");
                }

                long id = Check(await planning.RequestRephaseAsync(c.CompanyId, (long)i.Number("Budget"), i.Text("Account"), (int)i.Number("From"), (int)i.Number("To"), i.Number("Amount"),
                    i.Text("Reason"), c.UserId).ConfigureAwait(true));
                return $"Re-phasing {id} requested.";
            },
            List = async c => Table(await c.Db.Set<BudgetRephase>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Id", r => r.Id), ("Budget", r => r.BudgetId), ("Account", r => r.AccountCode), ("From", r => r.FromPeriod), ("To", r => r.ToPeriod), ("Amount", r => M(r.Amount)),
                ("Reason", r => r.Reason), ("Status", r => r.Status)),
        };
    }

    private static async Task<DataTable> BudgetsInPreparationAsync(OperationContext c)
    {
        // A department's preparers see their own department's budgets only (Finance sees every department).
        IReadOnlySet<string>? departments = await c.Get<IBudgetPreparationService>().DepartmentsForAsync(c.CompanyId, c.UserId).ConfigureAwait(true);
        List<string> visible = departments is null ? [] : [.. departments.Select(d => d.ToUpperInvariant())];
        return Table(await c.Db.Set<Budget>().AsNoTracking().Include(b => b.Lines)
                .Where(b => b.CompanyId == c.CompanyId && (departments == null || (b.Department != null && visible.Contains(b.Department.ToUpper()))))
                .OrderByDescending(b => b.Id).Take(200).ToListAsync().ConfigureAwait(true),
            ("Id", b => b.Id), ("Budget", b => $"{b.Label} v{b.VersionNumber}"), ("Type", b => b.Type), ("Department", b => b.Department), ("Status", b => b.Status), ("Round", b => b.Round),
            ("Total", b => M(b.AnnualTotal)), ("Resolution", b => b.ResolutionReference), ("Active", b => b.IsActive));
    }
}
