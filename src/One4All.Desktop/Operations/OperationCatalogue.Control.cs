using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Expenses;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Expenses;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Control operations: staff expense claims, treasury transfers between bank accounts, budget control (virements, the vote book, utilisation alerts, year-end commitments, the seal), variance commentary and statement despatch.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Control()
    {
        const string people = "HR & Attendance", banking = "Banking", budgeting = "Budgeting", customers = "Customers";
        yield return new Operation
        {
            Module = people, Title = "Expense claims", ListTitle = "Claims",
            Description = "A staff member's claim for out-of-pocket expenses (lines DESCRIPTION:ACCOUNT:amount; …), approved by a user other than the claimant, rejected, "
                + "or reimbursed from a bank or cash account.",
            Fields = [Pick("Step", "Step", ["Create", "Approve", "Reject", "Reimburse"]), Req("Reference", "Claim reference"), Opt("Employee", "Employee number (create)"),
                Opt("Lines", "Lines: description:ACCOUNT:amount; … (create)"), Opt("Funding", "Paid from account (reimburse)"), Opt("Document", "Payment document number (reimburse)"), Today()],
            Run = async (c, i) =>
            {
                IExpenseClaimService claims = c.Get<IExpenseClaimService>();
                string reference = i.Text("Reference").Trim();
                if (i.Text("Step") == "Create")
                {
                    List<ExpenseClaimLineRequest> lines = [];
                    foreach (KeyValuePair<string, decimal> pair in Pairs(i.Optional("Lines"), "description:ACCOUNT:amount"))
                    {
                        int colon = pair.Key.LastIndexOf(':');
                        lines.Add(colon > 0 ? new ExpenseClaimLineRequest(pair.Key[..colon].Trim(), pair.Key[(colon + 1)..].Trim(), pair.Value)
                            : throw new OperationException($"Write each line as description:ACCOUNT:amount (\"{pair.Key}\")."));
                    }

                    return Check(await claims.CreateAsync(c.CompanyId, await EmployeeAsync(c, i.Optional("Employee") ?? string.Empty).ConfigureAwait(true), reference, i.Date("Date"), lines).ConfigureAwait(true),
                        _ => $"Claim {reference} for {M(lines.Sum(l => l.Amount))} awaits approval.");
                }

                long claim = await FindAsync(c.Db.ExpenseClaims, x => x.CompanyId == c.CompanyId && x.Reference.ToUpper() == reference.ToUpper(), x => x.Id, "claim", reference).ConfigureAwait(true);
                return i.Text("Step") switch
                {
                    "Approve" => Check(await claims.ApproveAsync(c.CompanyId, claim, c.UserId).ConfigureAwait(true), $"Claim {reference} approved."),
                    "Reject" => Check(await claims.RejectAsync(c.CompanyId, claim).ConfigureAwait(true), $"Claim {reference} rejected."),
                    _ => Check(await claims.ReimburseAsync(c.CompanyId, claim, i.Date("Date"), i.Optional("Funding") ?? string.Empty, i.Optional("Document") ?? reference).ConfigureAwait(true),
                        j => $"Claim {reference} reimbursed ({j})."),
                };
            },
            List = async c => Table(await c.Db.ExpenseClaims.AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Claim", x => x.Reference), ("Employee", x => x.EmployeeId), ("Date", x => D(x.ClaimDate)), ("Amount", x => M(x.Lines.Sum(l => l.Amount))), ("Status", x => x.Status),
                ("Journal", x => x.JournalNumber ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = banking, Title = "Transfer between bank accounts",
            Description = "Moves money from one bank account to another, through a clearing account when it arrives later; across currencies, the amount received sets "
                + "the rate and the base-currency difference posts as an exchange gain or loss.",
            Fields = [Req("From", "From bank account"), Req("To", "To bank account"), Num("Sent", "Amount sent"), OptNum("Received", "Amount received (another currency)"), Req("Reference", "Reference"),
                Opt("Clearing", "Clearing (in transit) account"), Opt("Gain", "Exchange gain account"), Opt("Loss", "Exchange loss account"), Today()],
            Run = async (c, i) =>
            {
                BankTransferResult moved = Check(await c.Get<ITreasuryService>().TransferAsync(new BankTransferRequest
                {
                    CompanyId = c.CompanyId, FromBankAccountId = await BankAsync(c, i.Text("From")).ConfigureAwait(true), ToBankAccountId = await BankAsync(c, i.Text("To")).ConfigureAwait(true),
                    Date = i.Date("Date"), AmountSent = i.Number("Sent"), AmountReceived = i.OptionalNumber("Received"), Reference = i.Text("Reference"), ClearingAccountCode = i.Optional("Clearing"),
                    FxGainAccountCode = i.Optional("Gain"), FxLossAccountCode = i.Optional("Loss"),
                }).ConfigureAwait(true));
                return $"Transferred ({string.Join(", ", moved.JournalNumbers)}); base {M(moved.BaseSent)} sent, {M(moved.BaseReceived)} received"
                    + (moved.FxGainLoss == 0m ? "." : $", exchange {(moved.FxGainLoss > 0m ? "gain" : "loss")} {M(Math.Abs(moved.FxGainLoss))}.");
            },
        };
        yield return new Operation
        {
            Module = budgeting, Title = "Virements", ListTitle = "Virements",
            Description = "Moves budget from one vote to another within an approved budget: requested with a justification, approved or rejected by another user; "
                + "the register shows each vote's original, virements in and out, and current amount.",
            Fields = [Pick("Step", "Step", ["Request", "Approve", "Reject", "Register"]), OptNum("Budget", "Budget id (request, register)"), Opt("From", "From account"), Opt("To", "To account"),
                Num("Period", "Period", "1"), Num("Amount", "Amount", "0"), Opt("Why", "Justification / rejection reason"), OptNum("Virement", "Virement id (approve, reject)")],
            Run = async (c, i) =>
            {
                IBudgetControlService control = c.Get<IBudgetControlService>();
                long budget = (long)(i.OptionalNumber("Budget") ?? 0m), virement = (long)(i.OptionalNumber("Virement") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Request":
                        return Check(await control.RequestVirementAsync(c.CompanyId, budget, new VirementRequest(i.Optional("From") ?? string.Empty, i.Optional("To") ?? string.Empty, i.Whole("Period"),
                            i.Number("Amount"), i.Optional("Why") ?? string.Empty), c.UserId).ConfigureAwait(true), id => $"Virement {id} awaits another user's approval.");
                    case "Approve":
                        return Check(await control.ApproveVirementAsync(c.CompanyId, virement, c.UserId).ConfigureAwait(true), "Virement approved; the budget is moved.");
                    case "Reject":
                        return Check(await control.RejectVirementAsync(c.CompanyId, virement, c.UserId, i.Optional("Why") ?? string.Empty).ConfigureAwait(true), "Virement rejected.");
                    default:
                        IReadOnlyList<VirementRegisterRow> rows = Check(await control.VirementRegisterAsync(c.CompanyId, budget).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, rows.Select(r => $"{r.AccountCode,-12} original {M(r.Original),14}  in {M(r.VirementsIn),12}  out {M(r.VirementsOut),12}  now {M(r.Current),14}"));
                }
            },
            List = async c => Table(await c.Db.Set<BudgetVirement>().AsNoTracking().Where(v => v.CompanyId == c.CompanyId).OrderByDescending(v => v.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Virement", v => v.Id), ("Budget", v => v.BudgetId), ("From", v => v.FromAccountCode), ("To", v => v.ToAccountCode), ("Period", v => v.Period), ("Amount", v => M(v.Amount)),
                ("Status", v => v.Status), ("Why", v => v.Justification)),
        };
        yield return new Operation
        {
            Module = budgeting, Title = "Vote book and utilisation alerts",
            Description = "The vote book to a period (original, supplementary, virements, expenditure, commitments and what is unexpended per vote), the votes past "
                + "utilisation thresholds (e.g. 75, 90, 100 %), or a check that an approved budget's seal still matches its figures.",
            Fields = [Pick("Step", "Step", ["Vote book", "Utilisation alerts", "Verify seal"]), Num("Year", "Financial year (starting in)", DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Num("Period", "As at period", "12"), Opt("Thresholds", "Thresholds %", "75, 90, 100"), OptNum("Budget", "Budget id (seal)")],
            Run = async (c, i) =>
            {
                IBudgetControlService control = c.Get<IBudgetControlService>();
                if (i.Text("Step") == "Verify seal")
                {
                    return Check(await control.VerifySealAsync(c.CompanyId, (long)(i.OptionalNumber("Budget") ?? 0m)).ConfigureAwait(true))
                        ? "The seal matches: the approved budget is unchanged." : "The seal does NOT match: the approved figures have been altered.";
                }

                long year = await YearAsync(c, i.Whole("Year")).ConfigureAwait(true);
                if (i.Text("Step") == "Vote book")
                {
                    IReadOnlyList<VoteBookRow> book = await control.VoteBookAsync(c.CompanyId, year, i.Whole("Period")).ConfigureAwait(true);
                    return book.Count == 0 ? "No votes." : string.Join(Environment.NewLine, book.Select(r => $"{r.AccountCode,-10} {r.Name,-24} appropriation {M(r.Appropriation),14}  spent {M(r.Expenditure),14}  "
                        + $"committed {M(r.Commitments),12}  unexpended {M(r.Unexpended),14}"));
                }

                List<decimal> thresholds = [.. (i.Optional("Thresholds") ?? "75, 90, 100").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal p) ? p
                        : throw new OperationException($"\"{t}\" is not a percentage."))];
                IReadOnlyList<UtilisationAlert> alerts = await control.UtilisationAlertsAsync(c.CompanyId, year, i.Whole("Period"), thresholds).ConfigureAwait(true);
                return alerts.Count == 0 ? "No vote is past a threshold." : string.Join(Environment.NewLine, alerts.Select(a => $"{a.AccountCode,-10} {a.Name,-24} {a.PercentUsed,6:0.0}% used "
                    + $"({M(a.Used)} of {M(a.Appropriation)}) — past {a.ThresholdCrossed:0.##}%"));
            },
        };
        yield return new Operation
        {
            Module = budgeting, Title = "Year-end commitments", ListTitle = "Open commitments",
            Description = "Closes the year's open commitments (orders not yet invoiced): those listed (ids) are carried forward into the next year's budget, the rest lapse.",
            Fields = [Num("Year", "Closing financial year (starting in)", DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)), Opt("Carry", "Carry forward: commitment ids, comma-separated")],
            Run = async (c, i) =>
            {
                int start = i.Whole("Year");
                List<long> carry = [.. (i.Optional("Carry") ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => long.TryParse(t, out long id) ? id : throw new OperationException($"\"{t}\" is not a commitment id."))];
                CommitmentYearEnd closed = Check(await c.Get<IBudgetControlService>().CloseYearCommitmentsAsync(c.CompanyId, await YearAsync(c, start).ConfigureAwait(true),
                    await YearAsync(c, start + 1).ConfigureAwait(true), carry, c.UserId).ConfigureAwait(true));
                return $"{closed.CarriedForward.Count} carried forward ({M(closed.CarriedForward.Sum(x => x.Amount))}); {closed.Lapsed.Count} lapsed ({M(closed.Lapsed.Sum(x => x.Amount))}).";
            },
            List = async c => Table(await c.Db.BudgetCommitments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.Status == CommitmentStatus.Open).OrderBy(x => x.AccountCode).Take(500)
                    .ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Year", x => x.FiscalYearId), ("Account", x => x.AccountCode), ("Reference", x => x.Reference), ("Period", x => x.Period), ("Amount", x => M(x.Amount))),
        };
        yield return new Operation
        {
            Module = budgeting, Title = "Variance commentary",
            Description = "The material variances between budget and actual to a period (over a value or a % threshold) with their explanations; a budget holder "
                + "explains one by account.",
            Fields = [Pick("Step", "Step", ["Material variances", "Explain"]), Num("Year", "Financial year (starting in)", DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Num("Period", "As at period", "12"), Pick("Type", "Budget type", Enum.GetNames<BudgetType>()), Opt("Department", "Department"), Num("Value", "Material over (value)", "1000"),
                Num("Percent", "or over (%)", "10"), Opt("Account", "Account (explain)"), Opt("Comment", "Explanation")],
            Run = async (c, i) =>
            {
                IVarianceCommentaryService commentary = c.Get<IVarianceCommentaryService>();
                long year = await YearAsync(c, i.Whole("Year")).ConfigureAwait(true);
                if (i.Text("Step") == "Explain")
                {
                    return Check(await commentary.CommentAsync(c.CompanyId, year, i.Whole("Period"), i.Optional("Account") ?? string.Empty, i.Optional("Comment") ?? string.Empty, c.UserId).ConfigureAwait(true),
                        "Explanation recorded.");
                }

                IReadOnlyList<MaterialVarianceRow> rows = Check(await commentary.MaterialVariancesAsync(c.CompanyId, year, Enum.Parse<BudgetType>(i.Text("Type")), i.Optional("Department"), i.Whole("Period"),
                    new VarianceThreshold(i.Number("Value"), i.Number("Percent"))).ConfigureAwait(true));
                return rows.Count == 0 ? "No material variances." : string.Join(Environment.NewLine, rows.Select(r => $"{r.AccountCode,-10} budget {M(r.Budget),14}  actual {M(r.Actual),14}  "
                    + $"variance {M(r.Variance),14}  {(r.AwaitingComment ? "AWAITING EXPLANATION" : r.Comment)}"));
            },
        };
        yield return new Operation
        {
            Module = customers, Title = "Email customer statements",
            Description = "Queues a statement email to every customer owing at least a minimum balance at a date, with an email template and language; reports how many "
                + "had no email address.",
            Fields = [Today("Date", "Statement date"), Req("Template", "Email template code"), Req("Language", "Language", "en"), Num("Minimum", "Smallest balance worth a statement", "0")],
            Run = async (c, i) =>
            {
                StatementDespatch queued = Check(await c.Get<IStatementDespatchService>().QueueAsync(c.CompanyId, i.Date("Date"), i.Text("Template"), i.Text("Language"), i.Number("Minimum")).ConfigureAwait(true));
                return $"{queued.Queued} statement(s) queued{(queued.BatchId is long b ? $" (batch {b})" : string.Empty)}; {queued.NoEmail} customer(s) with no email; {queued.BelowMinimum} below the minimum.";
            },
        };
    }

    // The financial year starting in a calendar year.
    private static async Task<long> YearAsync(OperationContext c, int startYear)
        => await c.Db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == c.CompanyId && y.StartDate.Year == startYear).Select(y => (long?)y.Id).FirstOrDefaultAsync().ConfigureAwait(true)
            ?? throw new OperationException($"No financial year starting in {startYear}.");
}
