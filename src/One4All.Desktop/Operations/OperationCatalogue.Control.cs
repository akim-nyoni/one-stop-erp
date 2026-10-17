using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Expenses;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Abstractions.Security;
using One4All.Application.Housing;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Expenses;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Control operations: staff expense claims, treasury transfers between bank accounts, budget control (virements, the vote book, utilisation alerts, year-end commitments, the seal), variance commentary, statement despatch, housing agreements and maps, and failed offline operations.</content>
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
            List = async c => Table(await c.Db.ExpenseClaims.AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
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
            Module = budgeting, Title = "Budget methods and forecasts", ListTitle = "Budgets",
            Description = "Builds a draft budget incrementally from a prior year's actuals or budget with an uplift (accounts the draft already has are left alone); reforecasts a budget "
                + "with the actuals to a period and the budget for the rest (a new Reforecast version); and shows its cash budget - each line turned into cash by its timing "
                + "(prefix:p0/p1/…; e.g. 4:50/30/20 collects sales over three months) from the opening cash.",
            Fields = [Pick("Step", "Step", ["Build from prior-year actuals", "Build from prior-year budget", "Reforecast", "Cash budget"]), Num("Budget", "Budget id"),
                OptNum("SourceYear", "Prior fiscal year id (build)"), Num("Uplift", "Uplift % (build)", "0"), Opt("Prefix", "Accounts starting (build; blank: all)"),
                Num("AsAt", "Actuals to period (reforecast)", "6"), Opt("Label", "Reforecast label"), Opt("Timings", "Cash timings: prefix:p0/p1/…; …"), Num("Opening", "Opening cash", "0")],
            Run = async (c, i) =>
            {
                IBudgetMethodService methods = c.Get<IBudgetMethodService>();
                long budget = (long)i.Number("Budget");
                switch (i.Text("Step"))
                {
                    case "Reforecast":
                        return Check(await methods.ReforecastAsync(c.CompanyId, budget, i.Whole("AsAt"), i.Optional("Label") ?? string.Empty).ConfigureAwait(true), id => $"Reforecast budget {id} created.");
                    case "Cash budget":
                        List<One4All.Application.Budgeting.CashTiming> timings = [];
                        foreach (string part in (i.Optional("Timings") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            timings.Add(bits.Length == 2
                                ? new One4All.Application.Budgeting.CashTiming(bits[0], [.. bits[1].Split('/').Select(p => decimal.Parse(p.Trim(), System.Globalization.CultureInfo.InvariantCulture))])
                                : throw new OperationException($"Write each timing as prefix:p0/p1/… (\"{part}\")."));
                        }

                        var cash = Check(await methods.CashBudgetAsync(c.CompanyId, budget, timings, i.Number("Opening")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, cash.Months.Select(m => $"P{m.Period,-3} opening {M(m.Opening),14}  in {M(m.Receipts),12}  out {M(m.Payments),12}  closing {M(m.Closing),14}"))
                            + Environment.NewLine + $"After the year end: {M(cash.CarryOut)}";
                    default:
                        return Check(await methods.BuildIncrementalAsync(new IncrementalBudgetRequest
                        {
                            CompanyId = c.CompanyId, BudgetId = budget, Basis = i.Text("Step").EndsWith("budget", StringComparison.Ordinal) ? IncrementalBasis.PriorYearBudget : IncrementalBasis.PriorYearActual,
                            SourceFiscalYearId = (long)(i.OptionalNumber("SourceYear") ?? 0m), UpliftPercent = i.Number("Uplift"), AccountPrefix = i.Optional("Prefix"),
                        }).ConfigureAwait(true), n => $"{n} line(s) added.");
                }
            },
            List = async c => Table(await c.Db.Set<Core.Budgeting.Budget>().AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Page(c, 100).ToListAsync().ConfigureAwait(true),
                ("Id", b => b.Id), ("Label", b => b.Label), ("Type", b => b.Type), ("Department", b => b.Department), ("Version", b => b.VersionNumber), ("Active", b => b.IsActive)),
        };
        yield return new Operation
        {
            Module = budgeting, Title = "Virements", ListTitle = "Virements",
            Description = "Moves budget from one vote to another - between cost centres, or to another department's budget of the same year - requested with a justification, approved "
                + "or rejected by another user whose authorisation level covers the amount's band (Bands: limit:level; …, blank limit for the top band); the register shows each "
                + "vote's original, virements in and out, and current amount.",
            Fields = [Pick("Step", "Step", ["Request", "Approve", "Reject", "Register", "Bands"]), OptNum("Budget", "Budget id (request, register)"), Opt("From", "From account"), Opt("To", "To account"),
                Opt("FromCentre", "From cost centre"), Opt("ToCentre", "To cost centre"), OptNum("ToBudget", "To budget id (another department)"),
                Num("Period", "Period", "1"), Num("Amount", "Amount", "0"), Opt("Why", "Justification / rejection reason"), OptNum("Virement", "Virement id (approve, reject)"),
                Opt("Bands", "Bands: limit:level; … (bands)")],
            Run = async (c, i) =>
            {
                IBudgetControlService control = c.Get<IBudgetControlService>();
                long budget = (long)(i.OptionalNumber("Budget") ?? 0m), virement = (long)(i.OptionalNumber("Virement") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Request":
                        return Check(await control.RequestVirementAsync(c.CompanyId, budget, new VirementRequest(i.Optional("From") ?? string.Empty, i.Optional("To") ?? string.Empty, i.Whole("Period"),
                            i.Number("Amount"), i.Optional("Why") ?? string.Empty, i.Optional("FromCentre"), i.Optional("ToCentre"), (long?)i.OptionalNumber("ToBudget")), c.UserId).ConfigureAwait(true),
                            id => $"Virement {id} awaits another user's approval.");
                    case "Bands":
                        List<One4All.Application.Budgeting.VirementBandRule> bands = [];
                        foreach (string part in (i.Optional("Bands") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            bands.Add(bits.Length == 2 && int.TryParse(bits[1], out int level)
                                ? new One4All.Application.Budgeting.VirementBandRule(bits[0].Length == 0 ? null : decimal.Parse(bits[0], System.Globalization.CultureInfo.InvariantCulture), level)
                                : throw new OperationException($"Write each band as limit:level (\"{part}\")."));
                        }

                        return Check(await control.SetVirementBandsAsync(c.CompanyId, bands).ConfigureAwait(true), bands.Count == 0 ? "Bands cleared." : "Bands saved.");
                    case "Approve":
                        return Check(await control.ApproveVirementAsync(c.CompanyId, virement, c.UserId).ConfigureAwait(true), "Virement approved; the budget is moved.");
                    case "Reject":
                        return Check(await control.RejectVirementAsync(c.CompanyId, virement, c.UserId, i.Optional("Why") ?? string.Empty).ConfigureAwait(true), "Virement rejected.");
                    default:
                        IReadOnlyList<VirementRegisterRow> rows = Check(await control.VirementRegisterAsync(c.CompanyId, budget).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, rows.Select(r => $"{r.AccountCode,-12} original {M(r.Original),14}  in {M(r.VirementsIn),12}  out {M(r.VirementsOut),12}  now {M(r.Current),14}"));
                }
            },
            List = async c => Table(await c.Db.Set<BudgetVirement>().AsNoTracking().Where(v => v.CompanyId == c.CompanyId).OrderByDescending(v => v.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
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
                    + $"({M(a.Used)} of {M(a.Appropriation)}) - past {a.ThresholdCrossed:0.##}%"));
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
            List = async c => Table(await c.Db.BudgetCommitments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.Status == CommitmentStatus.Open).OrderBy(x => x.AccountCode).Page(c, 500)
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
        yield return new Operation
        {
            Module = "Housing", Title = "Sale, lease and rental agreements", ListTitle = "Agreements",
            Description = "Drafts an agreement with a party for a stand (price, terms, special conditions), records its signing with witnesses, amends it as a new version "
                + "with a reason, shows its version history, or checks every stored agreement document against its hash.",
            Fields = [Pick("Step", "Step", ["Draft", "Sign", "Amend", "History", "Verify documents"]), Opt("Agreement", "Agreement number"), Pick("Type", "Type (draft)", Enum.GetNames<Core.Housing.AgreementType>()),
                Opt("Party", "Party customer code (draft)"), Opt("Stand", "Stand number"), Num("Price", "Price", "0"), Opt("Terms", "Terms"), Opt("Special", "Special conditions"),
                Opt("Witnesses", "Witnesses, comma-separated (sign)"), Opt("Reason", "Amendment reason"), Today()],
            Run = async (c, i) =>
            {
                IAgreementRegisterService register = c.Get<IAgreementRegisterService>();
                string number = (i.Optional("Agreement") ?? string.Empty).Trim();
                switch (i.Text("Step"))
                {
                    case "Draft":
                        long? stand = i.Optional("Stand") is string standNumber
                            ? await FindAsync(c.Db.HousingStands, s => s.CompanyId == c.CompanyId && s.StandNumber.ToUpper() == standNumber.Trim().ToUpper(), s => s.Id, "stand", standNumber).ConfigureAwait(true)
                            : null;
                        return Check(await register.DraftAsync(new AgreementDraft
                        {
                            CompanyId = c.CompanyId, AgreementType = Enum.Parse<Core.Housing.AgreementType>(i.Text("Type")), PartyCustomerId = await CustomerAsync(c, i.Optional("Party") ?? string.Empty).ConfigureAwait(true),
                            StandId = stand, Price = i.Number("Price"), Terms = i.Optional("Terms") ?? string.Empty, SpecialConditions = i.Optional("Special"), Date = i.Date("Date"),
                        }).ConfigureAwait(true), n => $"Agreement {n} drafted; sign it once executed.");
                    case "Sign":
                        string[] witnesses = (i.Optional("Witnesses") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        return Check(await register.SignAsync(c.CompanyId, number, i.Date("Date"), witnesses).ConfigureAwait(true), $"Agreement {number} signed.");
                    case "Amend":
                        return Check(await register.AmendAsync(c.CompanyId, number, i.Number("Price"), i.Optional("Terms") ?? string.Empty, i.Optional("Special"), i.Optional("Reason") ?? string.Empty)
                            .ConfigureAwait(true), v => $"Agreement {number} amended (version {v}).");
                    case "History":
                        IReadOnlyList<AgreementVersionEntry> history = Check(await register.HistoryAsync(c.CompanyId, number).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, history.Select(h => $"v{h.Version} {M(h.Price),14} {(h.InForce ? "IN FORCE" : string.Empty),-9} signed {(h.SignedOn is DateOnly d ? D(d) : "-")} "
                            + $"{h.Witnesses} {h.AmendmentReason}"));
                    default:
                        IReadOnlyList<string> problems = await register.VerifyDocumentsAsync(c.CompanyId).ConfigureAwait(true);
                        return problems.Count == 0 ? "Every agreement document matches its hash." : string.Join(Environment.NewLine, problems.Prepend("ALTERED OR MISSING:"));
                }
            },
            List = async c => Table(await c.Db.Set<Core.Housing.HousingAgreement>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Agreement", a => a.AgreementNumber), ("Type", a => a.AgreementType), ("Party", a => a.PartyCustomerId), ("Stand", a => a.StandId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = "Housing", Title = "Map stands and parcels",
            Description = "Records a stand's or land parcel's boundary (WKT POLYGON in longitude latitude), lists stands whose mapped area differs from the surveyed area "
                + "by more than a tolerance, or exports a layer as GeoJSON for a GIS.",
            Fields = [Pick("Step", "Step", ["Map a stand", "Map a parcel", "Area discrepancies", "Export GeoJSON"]), Opt("Number", "Stand / parcel number"), Opt("Wkt", "Boundary (WKT POLYGON)"),
                Num("Tolerance", "Tolerance %", "5"), Pick("Layer", "Layer (export)", Enum.GetNames<MapLayer>()), Opt("Scheme", "Scheme code (export; blank: all)"), Opt("File", "Save GeoJSON to (path)")],
            Run = async (c, i) =>
            {
                ISpatialRegisterService spatial = c.Get<ISpatialRegisterService>();
                string number = (i.Optional("Number") ?? string.Empty).Trim();
                static string Shown(GeoBoundary b) => $"Mapped: {b.AreaSquareMetres:#,0.##} m², centroid {b.Centroid.Longitude:0.######}, {b.Centroid.Latitude:0.######}.";
                switch (i.Text("Step"))
                {
                    case "Map a stand":
                        long standId = await FindAsync(c.Db.HousingStands, s => s.CompanyId == c.CompanyId && s.StandNumber.ToUpper() == number.ToUpper(), s => s.Id, "stand", number).ConfigureAwait(true);
                        return Shown(Check(await spatial.MapStandAsync(c.CompanyId, standId, i.Optional("Wkt") ?? string.Empty).ConfigureAwait(true)));
                    case "Map a parcel":
                        long parcelId = await FindAsync(c.Db.LandParcels, p => p.CompanyId == c.CompanyId && p.ParcelNumber.ToUpper() == number.ToUpper(), p => p.Id, "land parcel", number).ConfigureAwait(true);
                        return Shown(Check(await spatial.MapParcelAsync(c.CompanyId, parcelId, i.Optional("Wkt") ?? string.Empty).ConfigureAwait(true)));
                    case "Area discrepancies":
                        IReadOnlyList<AreaDiscrepancy> off = await spatial.DiscrepanciesAsync(c.CompanyId, i.Number("Tolerance")).ConfigureAwait(true);
                        return off.Count == 0 ? "Every mapped stand is within tolerance." : string.Join(Environment.NewLine, off.Select(d => $"{d.StandNumber,-12} surveyed {d.SurveyedSquareMetres,12:#,0.##} m²  "
                            + $"mapped {d.MappedSquareMetres,12:#,0.##} m²  {d.DifferencePercent,6:0.0}%"));
                    default:
                        long? scheme = i.Optional("Scheme") is string code
                            ? await FindAsync(c.Db.HousingSchemes, s => s.CompanyId == c.CompanyId && s.Code.ToUpper() == code.Trim().ToUpper(), s => s.Id, "scheme", code).ConfigureAwait(true)
                            : null;
                        string json = await spatial.GeoJsonAsync(c.CompanyId, Enum.Parse<MapLayer>(i.Text("Layer")), scheme).ConfigureAwait(true);
                        if (i.Optional("File") is string path)
                        {
                            await System.IO.File.WriteAllTextAsync(path, json).ConfigureAwait(true);
                            return $"GeoJSON saved to {path} ({json.Length:#,0} characters).";
                        }

                        return json;
                }
            },
        };
        yield return new Operation
        {
            Module = "System Manager", Title = "Failed offline operations", ListTitle = "Failed offline operations",
            Description = "Work captured offline that failed to synchronise: requeue one (after fixing the cause) or discard it, by its operation id.",
            Fields = [Pick("Step", "Step", ["Requeue", "Discard"]), Req("Operation", "Operation id")],
            Run = async (c, i) =>
            {
                Guid id = Guid.TryParse(i.Text("Operation"), out Guid parsed) ? parsed : throw new OperationException("That is not an operation id.");
                IOfflineAdminService offline = c.Get<IOfflineAdminService>();
                return i.Text("Step") == "Requeue"
                    ? Check(await offline.RequeueAsync(id).ConfigureAwait(true), "Requeued; it synchronises with the next run.")
                    : Check(await offline.DiscardAsync(id).ConfigureAwait(true), "Discarded.");
            },
            List = async c => Table(await c.Get<IOfflineAdminService>().ListFailedAsync().ConfigureAwait(true),
                ("Operation", o => o.OperationId), ("Kind", o => o.Kind), ("Created", o => o.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
                ("Attempts", o => o.Attempts), ("Error", o => o.LastError ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = "System Manager", Title = "Printers per workstation", ListTitle = "Printers",
            Description = "The printer, tray and copies a workstation uses for each kind of printing (reports, documents, receipts, cheques, labels, payslips). Workstation * is the "
                + "default for every computer without its own; this computer's name is filled in. The report viewer's print dialog opens on the chosen printer.",
            Fields = [Pick("Step", "Step", ["Set", "Remove"]), Req("Workstation", "Workstation (computer name, or *)", Environment.MachineName),
                Pick("Purpose", "Purpose", Enum.GetNames<Core.System.PrintPurpose>()),
                new OperationField("Printer", "Printer", FieldKind.Choice, Required: false, Choices: [], ChoicesFrom: _ => Task.FromResult(Reporting.PrintPreferences.InstalledPrinters())),
                Opt("Tray", "Tray (Auto, Manual, Cassette, Tractor)"), Num("Copies", "Copies", "1")],
            Run = async (c, i) =>
            {
                Application.Abstractions.System.IWorkstationPrinterService printers = c.Get<Application.Abstractions.System.IWorkstationPrinterService>();
                Core.System.PrintPurpose purpose = Enum.Parse<Core.System.PrintPurpose>(i.Text("Purpose"));
                return i.Text("Step") == "Set"
                    ? Check(await printers.SetAsync(c.CompanyId, new Application.Abstractions.System.PrinterSetting(i.Text("Workstation"), purpose, i.Optional("Printer") ?? string.Empty,
                        i.Optional("Tray"), i.Whole("Copies"))).ConfigureAwait(true), "Printer set.")
                    : Check(await printers.RemoveAsync(c.CompanyId, i.Text("Workstation"), purpose).ConfigureAwait(true), "Printer removed; the default applies.");
            },
            List = async c => Table(await c.Get<Application.Abstractions.System.IWorkstationPrinterService>().ListAsync(c.CompanyId).ConfigureAwait(true),
                ("Workstation", p => p.Workstation), ("Purpose", p => p.Purpose), ("Printer", p => p.PrinterName), ("Tray", p => p.Tray ?? "default"), ("Copies", p => p.Copies)),
        };
        yield return new Operation
        {
            Module = "System Manager", Title = "Archive an audit year", ListTitle = "Audit log by year",
            Description = "Moves a closed year of the audit log out of the live table into the archive schema (still readable, never deleted). It needs the "
                + "licence holder's signed authorisation for that year (MA1. token), years go oldest first, and the hash chain carries on across the gap.",
            Fields = [Num("Year", "Year to archive"), Req("Authorisation", "Licence holder's authorisation (MA1. token)")],
            Run = async (c, i) =>
            {
                AuditArchiveEntry archived = Check(await c.Get<IAuditArchiveService>().ArchiveYearAsync(i.Whole("Year"), i.Text("Authorisation"), c.UserId).ConfigureAwait(true));
                return $"{archived.Year} archived: {archived.RowCount:N0} entries now in {archived.TableName} (content hash {archived.ContentHash[..12]}…).";
            },
            List = async c => Table(await c.Get<IAuditArchiveService>().YearsAsync().ConfigureAwait(true),
                ("Year", y => y.Year), ("Entries", y => y.Rows), ("Archived", y => y.Archived)),
        };
        yield return new Operation
        {
            Module = people, Title = "Benefits in kind", ListTitle = "Benefits",
            Description = "Taxable benefits (a company car valued from the vehicle scale by engine size or cost, housing, school fees, medical): taxed each pay period they "
                + "apply and shown on the payslip as memo lines. Also the vehicle scale's bands, the deemed interest rate cheap staff loans are taxed at, and a loan's own rate.",
            Fields = [Pick("Step", "Step", ["Add benefit", "End benefit", "Vehicle scale band", "Deemed loan rate", "Loan interest rate", "Suspend loan deductions", "Resume loan deductions",
                    "Loan interest income account"]), EmployeePick("Employee", "Employee", required: false), Opt("Account", "Interest income account"),
                Pick("Kind", "Benefit", Enum.GetNames<Core.Payroll.BenefitKind>()), Opt("Description", "What it is (registration, property)"), OptNum("Engine", "Engine capacity (cc)"),
                OptNum("Cost", "Vehicle cost"), OptNum("Monthly", "Monthly value (blank: from the vehicle scale)"), Pick("Basis", "Scale measured on", Enum.GetNames<Core.Payroll.VehicleScaleBasis>()),
                OptNum("From", "Scale band from"), OptNum("To", "Scale band to (blank: open)"), OptNum("Rate", "Interest rate % a year"), Opt("Loan", "Loan reference"),
                OptNum("Benefit", "Benefit id (end)"), Today()],
            Run = async (c, i) =>
            {
                switch (i.Text("Step"))
                {
                    case "Add benefit":
                        long employee = await EmployeeAsync(c, i.Optional("Employee") ?? string.Empty).ConfigureAwait(true);
                        Core.Payroll.EmployeeBenefit benefit = new(c.CompanyId, employee, Enum.Parse<Core.Payroll.BenefitKind>(i.Text("Kind")), i.Optional("Description") ?? string.Empty, i.Date("Date"))
                        {
                            EngineCapacityCc = i.OptionalNumber("Engine") is decimal cc ? (int)cc : null, Cost = i.OptionalNumber("Cost"), MonthlyValue = i.OptionalNumber("Monthly"),
                        };
                        c.Db.EmployeeBenefits.Add(benefit);
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"Benefit {benefit.Id} recorded from {D(benefit.From)}.";
                    case "End benefit":
                        long id = (long)(i.OptionalNumber("Benefit") ?? 0m);
                        Core.Payroll.EmployeeBenefit ending = await c.Db.EmployeeBenefits.FirstOrDefaultAsync(b => b.CompanyId == c.CompanyId && b.Id == id).ConfigureAwait(true)
                            ?? throw new OperationException($"No benefit {id}.");
                        ending.To = i.Date("Date");
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"Benefit {id} ends on {D(i.Date("Date"))}.";
                    case "Vehicle scale band":
                        c.Db.VehicleBenefitScales.Add(new Core.Payroll.VehicleBenefitScale(c.CompanyId, Enum.Parse<Core.Payroll.VehicleScaleBasis>(i.Text("Basis")), i.OptionalNumber("From") ?? 0m,
                            i.OptionalNumber("To"), i.OptionalNumber("Monthly") ?? 0m, i.Date("Date")));
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return "Scale band saved.";
                    case "Deemed loan rate":
                        Core.System.Company company = await c.Db.Companies.FirstAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                        company.DeemedLoanInterestRatePercent = i.OptionalNumber("Rate");
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return company.DeemedLoanInterestRatePercent is decimal rate ? $"Cheap loans are taxed at a deemed {rate}% a year." : "No loan benefit is taxed.";
                    case "Loan interest income account":
                        Core.System.Company owner = await c.Db.Companies.FirstAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                        owner.StaffLoanInterestAccountCode = i.Optional("Account")?.Trim().ToUpperInvariant();
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"Staff loan interest is credited to {owner.StaffLoanInterestAccountCode ?? "(none)"}.";
                    case "Suspend loan deductions":
                    case "Resume loan deductions":
                        string held = (i.Optional("Loan") ?? string.Empty).Trim();
                        Core.Payroll.EmployeeLoan pausing = await c.Db.EmployeeLoans.FirstOrDefaultAsync(l => l.CompanyId == c.CompanyId && l.Reference.ToUpper() == held.ToUpper()).ConfigureAwait(true)
                            ?? throw new OperationException($"No loan {held}.");
                        pausing.Suspended = i.Text("Step") == "Suspend loan deductions";
                        pausing.DeductFromPay = true;
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return pausing.Suspended ? $"Loan {pausing.Reference} is not deducted until resumed (interest still accrues)." : $"Loan {pausing.Reference} is deducted from pay each run.";
                    default:
                        string reference = (i.Optional("Loan") ?? string.Empty).Trim();
                        Core.Payroll.EmployeeLoan loan = await c.Db.EmployeeLoans.FirstOrDefaultAsync(l => l.CompanyId == c.CompanyId && l.Reference.ToUpper() == reference.ToUpper()).ConfigureAwait(true)
                            ?? throw new OperationException($"No loan {reference}.");
                        loan.InterestRatePercent = i.OptionalNumber("Rate") ?? 0m;
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"Loan {loan.Reference} charges {loan.InterestRatePercent}% a year.";
                }
            },
            List = async c => Table(await c.Db.EmployeeBenefits.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderBy(b => b.EmployeeId).ToListAsync().ConfigureAwait(true),
                ("Id", b => b.Id), ("Employee", b => b.EmployeeId), ("Benefit", b => b.Kind), ("What", b => b.Description), ("Engine cc", b => b.EngineCapacityCc?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                ("Monthly", b => b.MonthlyValue is decimal m ? M(m) : "scale"), ("From", b => D(b.From)), ("To", b => b.To is DateOnly t ? D(t) : string.Empty)),
        };
    }

    // The financial year starting in a calendar year.
    private static async Task<long> YearAsync(OperationContext c, int startYear)
        => await c.Db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == c.CompanyId && y.StartDate.Year == startYear).Select(y => (long?)y.Id).FirstOrDefaultAsync().ConfigureAwait(true)
            ?? throw new OperationException($"No financial year starting in {startYear}.");
}
