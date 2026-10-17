using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>General ledger: manual journals (maker-checker), funds and projects, fund accounting, and budgets by fund / project / branch.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Ledger()
    {
        const string module = "General Ledger";
        yield return new Operation
        {
            Module = module, Title = "Exchange-rate policy", ListTitle = "Rate policies",
            Description = "Which rate type a document type (SalesInvoice, Receipt, SalesCreditNote, PurchaseInvoice, PurchaseCreditNote, Payment) or a whole ledger (AR, AP) uses "
                + "when no rate is keyed. A trader's own rate type still comes first; with no policy the daily rate is used.",
            Fields = [Pick("Document", "Document type or ledger", ["SalesInvoice", "Receipt", "SalesCreditNote", "PurchaseInvoice", "PurchaseCreditNote", "Payment", "AR", "AP"]),
                Pick("RateType", "Rate type", Enum.GetNames<ExchangeRateType>()), Tick("Remove", "Remove the policy")],
            Run = async (c, i) =>
            {
                string document = i.Text("Document");
                ExchangeRatePolicy? policy = await c.Db.ExchangeRatePolicies.FirstOrDefaultAsync(p => p.CompanyId == c.CompanyId && p.DocumentType == document).ConfigureAwait(true);
                if (i.Flag("Remove"))
                {
                    if (policy is not null)
                    {
                        c.Db.ExchangeRatePolicies.Remove(policy);
                    }
                }
                else if (policy is null)
                {
                    c.Db.ExchangeRatePolicies.Add(new ExchangeRatePolicy(c.CompanyId, document, Enum.Parse<ExchangeRateType>(i.Text("RateType"))));
                }
                else
                {
                    policy.RateType = Enum.Parse<ExchangeRateType>(i.Text("RateType"));
                }

                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return i.Flag("Remove") ? $"{document} uses the default rate again." : $"{document} now uses the {i.Text("RateType")} rate.";
            },
            List = async c => Table(await c.Db.ExchangeRatePolicies.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.DocumentType).ToListAsync().ConfigureAwait(true),
                ("Document", p => p.DocumentType), ("Rate type", p => p.RateType)),
        };
        yield return new Operation
        {
            Module = module, Title = "Year-end", ListTitle = "Fiscal years",
            Description = "Run: rolls the year's profit or loss to retained earnings (in the adjustment period when there is one), opens the next year on the same calendar and closes "
                + "the year. Reverse: undoes a year-end not yet hard-locked so adjustments can be made and it can be run again. Hard lock: makes it final — every period permanently closed.",
            Fields = [Num("Year", "Fiscal year id"), Pick("Step", "Step", ["Run", "Reverse", "Hard lock"]), Req("Retained", "Retained earnings account", "3200"), Opt("Reason", "Reason (reverse)")],
            Run = async (c, i) =>
            {
                IYearEndService yearEnd = c.Get<IYearEndService>();
                long year = i.Whole("Year");
                switch (i.Text("Step"))
                {
                    case "Reverse":
                        string journal = Check(await yearEnd.ReverseAsync(c.CompanyId, year, i.Optional("Reason") ?? string.Empty).ConfigureAwait(true));
                        return $"Year-end reversed (closing journal reversed by {journal}); the year is open again — reopen its periods to adjust.";
                    case "Hard lock":
                        _ = Check(await yearEnd.HardLockAsync(c.CompanyId, year).ConfigureAwait(true), string.Empty);
                        return "Year hard-locked: its periods are permanently closed.";
                    default:
                        YearEndResult result = Check(await yearEnd.RunAsync(c.CompanyId, year, i.Text("Retained")).ConfigureAwait(true));
                        return $"Year closed: net profit {result.NetProfitClosed:N2} to retained earnings by journal {result.ClosingJournalNumber}; next year id {result.NextYearId}.";
                }
            },
            List = async c => Table(await c.Db.FiscalYears.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.StartDate).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("From", x => D(x.StartDate)), ("To", x => D(x.EndDate)), ("Closed", x => x.IsClosed), ("Hard-locked", x => x.IsHardLocked)),
        };
        yield return new Operation
        {
            Module = module, Title = "Submit a manual journal", ListTitle = "Journals awaiting approval",
            Description = "Lines separated by ';', each ACCOUNT Dr|Cr AMOUNT with optional cc=, dept=, project=, fund=, branch= and a narrative after '#'; e.g. "
                + "\"6100 Dr 500 project=P-DAM fund=WATER # Contractor; 2410 Cr 500 fund=WATER\". It is validated and numbered now, and posts once someone else approves it.",
            Fields = [Today("Date", "Posting date"), Req("Narrative", "Narrative"), Opt("Reference", "Reference"), Req("Lines", "Lines"), Opt("Currency", "Currency (blank: base)")],
            Run = async (c, i) =>
            {
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Companies.AsNoTracking().Where(x => x.Id == c.CompanyId).Select(x => x.BaseCurrency).SingleAsync().ConfigureAwait(true);
                PostingReceipt receipt = Check(await c.Get<IJournalApprovalService>().SubmitAsync(new JournalRequest
                {
                    CompanyId = c.CompanyId, SourceModule = "GL", PostingDate = i.Date("Date"), TransactionCurrency = currency, Narrative = i.Text("Narrative"), Reference = i.Optional("Reference"),
                    DocumentType = "Manual", Lines = JournalLines(i.Text("Lines")),
                }, c.UserId).ConfigureAwait(true));
                return $"Journal {receipt.JournalNumber} submitted ({M(receipt.TotalBase)}); it posts when approved under Transactions → Journals Awaiting Approval.";
            },
            List = async c => Table(await c.Get<IJournalApprovalService>().AwaitingApprovalAsync(c.CompanyId).ConfigureAwait(true), ("Journal", j => j.JournalNumber),
                ("Date", j => D(j.PostingDate)), ("Narrative", j => j.Narrative), ("Value", j => M(j.TotalBaseDebit)), ("Prepared by", j => j.PreparedByUserId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Funds, projects and fund accounting", ListTitle = "Funds and projects",
            Description = "Add or change a fund (vote) or a project (closing a project stops postings to it), or turn fund accounting on or off: then every journal line names a fund — the default "
                + "fund fills sub-ledger postings — and each fund must balance within each journal.",
            Fields = [Pick("What", "What", ["Fund", "Project", "Fund accounting on", "Fund accounting off"]), Opt("Code", "Code (default fund for 'on')"), Opt("Name", "Name"),
                Tick("Restricted", "Restricted fund"), Opt("Fund", "Project's fund"), Tick("Closed", "Inactive / closed")],
            Run = async (c, i) =>
            {
                IMasterDataService master = c.Get<IMasterDataService>();
                switch (i.Text("What"))
                {
                    case "Fund":
                        _ = Check(await master.SaveFundAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), i.Flag("Restricted"), !i.Flag("Closed")).ConfigureAwait(true));
                        return $"Fund {i.Text("Code").ToUpperInvariant()} saved.";
                    case "Project":
                        _ = Check(await master.SaveProjectAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), i.Optional("Fund"), !i.Flag("Closed")).ConfigureAwait(true));
                        return $"Project {i.Text("Code").ToUpperInvariant()} saved.";
                    default:
                        bool on = i.Text("What") == "Fund accounting on";
                        string? fallback = i.Optional("Code")?.Trim().ToUpperInvariant();
                        if (on && fallback is not null && !await c.Db.Funds.AnyAsync(f => f.CompanyId == c.CompanyId && f.Code == fallback).ConfigureAwait(true))
                        {
                            throw new OperationException($"Fund {fallback} does not exist.");
                        }

                        Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                        company.FundAccounting = on;
                        company.DefaultFund = on ? fallback : company.DefaultFund;
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return on ? $"Fund accounting is on{(fallback is null ? " (no default fund: every line must name one)" : $"; unnamed lines go to {fallback}")}." : "Fund accounting is off.";
                }
            },
            List = async c =>
            {
                var funds = await c.Db.Funds.AsNoTracking().Where(f => f.CompanyId == c.CompanyId).Select(f => new { Kind = "Fund", f.Code, f.Name, Detail = f.IsRestricted ? "restricted" : string.Empty, f.IsActive })
                    .ToListAsync().ConfigureAwait(true);
                var projects = await c.Db.Projects.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).Select(p => new { Kind = "Project", p.Code, p.Name, Detail = p.FundCode ?? string.Empty, p.IsActive })
                    .ToListAsync().ConfigureAwait(true);
                return Table([.. funds, .. projects], ("Kind", r => r.Kind), ("Code", r => r.Code), ("Name", r => r.Name), ("Restricted / fund", r => r.Detail), ("Active", r => r.IsActive));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Reporting trees", ListTitle = "Reporting trees",
            Description = "Statement layouts independent of account codes (statutory, management, consolidation): create a tree (optionally started from the chart's types and categories), "
                + "add headings under parents, map account ranges, check every account is mapped once, and show the rolled-up statement (Statement by Reporting Tree, GL-TREE, prints it).",
            Fields = [Pick("Step", "Step", ["Create", "Create from chart", "Heading", "Map accounts", "Check", "Show statement"]), Req("Tree", "Tree code"), Opt("Name", "Tree / heading name"),
                Pick("Purpose", "Purpose", Enum.GetNames<ReportingTreePurpose>()), Opt("Node", "Heading code"), Opt("Parent", "Parent heading"), Num("Order", "Order", "10"),
                Tick("Credit", "Show credit-positive"), Opt("From", "From account"), Opt("To", "To account"), Opt("Start", "From date (blank: balance)"), Today("End", "To date")],
            Run = async (c, i) =>
            {
                IReportingTreeService trees = c.Get<IReportingTreeService>();
                string tree = i.Text("Tree");
                switch (i.Text("Step"))
                {
                    case "Create":
                    case "Create from chart":
                        _ = Check(await trees.CreateTreeAsync(c.CompanyId, tree, i.Optional("Name") ?? tree, Enum.Parse<ReportingTreePurpose>(i.Text("Purpose"))).ConfigureAwait(true));
                        return i.Text("Step") == "Create" ? $"Tree {tree.ToUpperInvariant()} created." : $"Tree {tree.ToUpperInvariant()} created with {Check(await trees.SeedFromChartAsync(c.CompanyId, tree).ConfigureAwait(true))} heading(s).";
                    case "Heading":
                        _ = Check(await trees.SaveNodeAsync(c.CompanyId, tree, i.Text("Node"), i.Optional("Name") ?? i.Text("Node"), i.Optional("Parent"), i.Whole("Order"), i.Flag("Credit")).ConfigureAwait(true), string.Empty);
                        return "Heading saved.";
                    case "Map accounts":
                        _ = Check(await trees.MapAsync(c.CompanyId, tree, i.Text("Node"), i.Text("From"), i.Optional("To")).ConfigureAwait(true), string.Empty);
                        return "Accounts mapped.";
                    case "Check":
                        ReportingTreeCheck check = Check(await trees.CheckAsync(c.CompanyId, tree).ConfigureAwait(true));
                        return check.IsComplete ? "Every active account is mapped exactly once." : string.Join(Environment.NewLine, new[]
                        {
                            check.Unmapped.Count == 0 ? null : $"Not mapped: {string.Join(", ", check.Unmapped)}",
                            check.MappedTwice.Count == 0 ? null : "Mapped twice: " + string.Join("; ", check.MappedTwice.Select(m => $"{m.Account} ({string.Join(", ", m.Nodes)})")),
                            check.Orphans.Count == 0 ? null : $"Headings without their parent: {string.Join(", ", check.Orphans)}",
                        }.Where(l => l is not null));
                    default:
                        DateOnly? start = i.Optional("Start") is string s ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                        IReadOnlyList<ReportingTreeLine> lines = Check(await trees.RollupAsync(c.CompanyId, tree, start, i.Date("End")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, lines.Select(l => $"{new string(' ', l.Level * 4)}{l.Name,-50} {M(l.Amount),16}"));
                }
            },
            List = async c => Table(await c.Db.ReportingTrees.AsNoTracking().Include(t => t.Nodes).Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.Code).ToListAsync().ConfigureAwait(true),
                ("Tree", t => t.Code), ("Name", t => t.Name), ("Purpose", t => t.Purpose), ("Headings", t => t.Nodes.Count)),
        };
        yield return new Operation
        {
            Module = module, Title = "Tax exemption certificates", ListTitle = "Certificates expiring within 30 days (or lately expired)",
            Description = "Record a customer's or supplier's exemption certificate (while valid no tax is charged either way; an exempt tax code needs one), or a supplier's tax clearance "
                + "(while valid no withholding is deducted from payments), or withdraw one by its id.",
            Fields = [Pick("Step", "Step", ["Record", "Withdraw", "Set default withholding"]), Pick("Ledger", "Who", ["Customer", "Supplier"]), Pick("Purpose", "For", ["Tax exemption", "Tax clearance (supplier)"]),
                Opt("Trader", "Customer / supplier code"), Opt("Number", "Certificate number"),
                Today("From", "Valid from"), Opt("To", "Valid to (yyyy-MM-dd)"), Opt("IssuedBy", "Issued by"), OptNum("Id", "Certificate id (withdraw)"),
                Opt("Wht", "Withholding code for suppliers without clearance (blank: none)")],
            Run = async (c, i) =>
            {
                ITaxComplianceService tax = c.Get<ITaxComplianceService>();
                if (i.Text("Step") == "Set default withholding")
                {
                    string? wht = i.Optional("Wht")?.Trim().ToUpperInvariant();
                    if (wht is not null && !await c.Db.TaxCodes.AnyAsync(t => t.CompanyId == c.CompanyId && t.Code == wht && t.Type == TaxType.Withholding && t.IsActive).ConfigureAwait(true))
                    {
                        throw new OperationException($"{wht} is not an active withholding tax code.");
                    }

                    (await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true)).WithholdingWithoutClearanceTaxCode = wht;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return wht is null ? "Payments withhold tax only where they ask for it." : $"Payments to suppliers without a valid tax clearance now withhold {wht} unless they name another code.";
                }

                if (i.Text("Step") == "Withdraw")
                {
                    _ = Check(await tax.RevokeCertificateAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0), i.Date("From")).ConfigureAwait(true), string.Empty);
                    return "Certificate withdrawn from that date.";
                }

                long id = Check(await tax.RecordCertificateAsync(c.CompanyId, i.Text("Ledger") == "Customer" ? Core.Trading.TradingLedger.AccountsReceivable : Core.Trading.TradingLedger.AccountsPayable,
                    i.Text("Trader"), i.Text("Number"), i.Date("From"), DateOnly.ParseExact(i.Text("To"), "yyyy-MM-dd", CultureInfo.InvariantCulture), i.Optional("IssuedBy"),
                    i.Text("Purpose").StartsWith("Tax clearance", StringComparison.Ordinal) ? CertificatePurpose.TaxClearance : CertificatePurpose.TaxExemption).ConfigureAwait(true));
                return $"Certificate {id} recorded.";
            },
            List = async c => Table(await c.Get<ITaxComplianceService>().ExpiringAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Id", e => e.CertificateId), ("Who", e => e.Ledger == Core.Trading.TradingLedger.AccountsReceivable ? "Customer" : "Supplier"), ("For", e => e.Purpose == CertificatePurpose.TaxClearance ? "Clearance" : "Exemption"),
                ("Code", e => e.TraderCode), ("Name", e => e.TraderName),
                ("Certificate", e => e.CertificateNumber), ("Valid to", e => D(e.ValidTo)), ("Days left", e => e.DaysLeft)),
        };
        yield return new Operation
        {
            Module = module, Title = "VAT return by tax point basis", ListTitle = "Journals awaiting approval",
            Description = "The VAT due for a period on the company's basis — invoice date (the ledger), tax point (deliveries move invoices between periods), or cash accounting (invoice VAT due as it "
                + "is paid) — with every adjustment off the ledger listed. Tick 'Make it the company's basis' to change the basis used from now on.",
            Fields = [Opt("From", "From (yyyy-MM-dd)"), Today("To", "To"), Pick("Basis", "Basis", Enum.GetNames<TaxPointBasis>()), Tick("Adopt", "Make it the company's basis")],
            Run = async (c, i) =>
            {
                TaxPointBasis basis = Enum.Parse<TaxPointBasis>(i.Text("Basis"));
                if (i.Flag("Adopt"))
                {
                    (await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true)).TaxPointBasis = basis;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                }

                DateOnly to = i.Date("To");
                DateOnly from = i.Optional("From") is string f ? DateOnly.ParseExact(f, "yyyy-MM-dd", CultureInfo.InvariantCulture) : new DateOnly(to.Year, to.Month, 1);
                BasisVatReturn vat = await c.Get<ITaxComplianceService>().VatReturnAsync(c.CompanyId, from, to, basis).ConfigureAwait(true);
                return string.Join(Environment.NewLine, vat.Adjustments.Select(a => $"  {a.Document,-14} {a.Reason,-40} output {M(a.Output),12}  input {M(a.Input),12}")
                    .Prepend($"{vat.Basis} basis, {D(vat.FromDate)} to {D(vat.ToDate)}: ledger output {M(vat.LedgerOutput)}, input {M(vat.LedgerInput)}")
                    .Append($"Output tax {M(vat.OutputTax)} − input tax {M(vat.InputTax)} = {(vat.NetPayable >= 0 ? "payable" : "refundable")} {M(Math.Abs(vat.NetPayable))}"
                        + (i.Flag("Adopt") ? $" ({basis} is now the company's basis)" : string.Empty)));
            },
            List = async c => Table(await c.Get<IJournalApprovalService>().AwaitingApprovalAsync(c.CompanyId).ConfigureAwait(true), ("Journal", j => j.JournalNumber), ("Date", j => D(j.PostingDate)),
                ("Narrative", j => j.Narrative), ("Value", j => M(j.TotalBaseDebit))),
        };
        yield return new Operation
        {
            Module = "Budgeting", Title = "Budget vs actual by fund, project or branch", ListTitle = "Budgets in preparation",
            Description = "The active budget against posted actuals for each fund, project or branch and account, up to a period (one value only, or all).",
            Fields = [Num("Year", "Fiscal year (start year)", DateTime.Today.Year.ToString(CultureInfo.InvariantCulture)), Pick("Type", "Budget type", Enum.GetNames<BudgetType>()),
                Opt("Department", "Department (blank: company-wide)"), Pick("Dimension", "By", Enum.GetNames<BudgetDimension>()), Opt("Value", "Only this code"), Num("Period", "Up to period", "12")],
            Run = async (c, i) =>
            {
                int year = i.Whole("Year");
                long yearId = await c.Db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == c.CompanyId && y.StartDate.Year == year).Select(y => (long?)y.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                    ?? throw new OperationException($"No fiscal year starting in {year}.");
                IReadOnlyList<DimensionBudgetRow> rows = Check(await c.Get<IBudgetingService>().BudgetVsActualByDimensionAsync(c.CompanyId, yearId, Enum.Parse<BudgetType>(i.Text("Type")),
                    i.Optional("Department"), Enum.Parse<BudgetDimension>(i.Text("Dimension")), i.Optional("Value"), i.Whole("Period")).ConfigureAwait(true));
                return rows.Count == 0 ? "Nothing budgeted or posted." : string.Join(Environment.NewLine, rows.Select(r =>
                    $"{(r.Value.Length == 0 ? "(none)" : r.Value),-12} {r.AccountCode,-8} budget {M(r.Budget),14}  actual {M(r.Actual),14}  remaining {M(r.Remaining),14}"));
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = "Budgeting", Title = "Multi-year framework, archive and lock", ListTitle = "Budget versions",
            Description = "Outer years: indicative amounts for the years after the budget year (\"1:6100:1300; 2:6100:1400\" — years after:account:amount), set before approval. "
                + "Show: the framework by account. Roll forward: next year's draft from an approved framework. Archive a superseded version (seal verified), then Lock it for good.",
            Fields = [Pick("Step", "Step", ["Outer years", "Show", "Roll forward", "Archive", "Lock"]), Num("Budget", "Budget id"), Opt("Years", "Outer years (outer years step)"),
                OptNum("NextYear", "Next fiscal year (start year, roll forward)"), Opt("Label", "Label for the new draft")],
            Run = async (c, i) =>
            {
                IBudgetFrameworkService framework = c.Get<IBudgetFrameworkService>();
                long budget = (long)i.Number("Budget");
                switch (i.Text("Step"))
                {
                    case "Outer years":
                        List<OuterYearAmount> amounts = [];
                        foreach (string part in (i.Optional("Years") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            amounts.Add(bits.Length == 3 && int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
                                && decimal.TryParse(bits[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
                                ? new OuterYearAmount(offset, bits[1], amount) : throw new OperationException($"Write each as years after:account:amount (\"{part}\")."));
                        }

                        return Check(await framework.SetOuterYearsAsync(c.CompanyId, budget, amounts).ConfigureAwait(true), "Outer years saved.");
                    case "Show":
                        IReadOnlyList<FrameworkRow> rows = Check(await framework.FrameworkAsync(c.CompanyId, budget).ConfigureAwait(true));
                        return rows.Count == 0 ? "No lines." : string.Join(Environment.NewLine, rows.Select(r => $"{r.AccountCode,-8} {r.CostCentre,-8} " + string.Join("  ", r.Years.Select(M))));
                    case "Roll forward":
                        int year = (int)(i.OptionalNumber("NextYear") ?? throw new OperationException("Give the next fiscal year."));
                        long yearId = await c.Db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == c.CompanyId && y.StartDate.Year == year).Select(y => (long?)y.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                            ?? throw new OperationException($"No fiscal year starting in {year}.");
                        return Check(await framework.RollForwardAsync(c.CompanyId, budget, yearId, i.Optional("Label") ?? string.Empty).ConfigureAwait(true), id => $"Draft budget {id} created for {year}.");
                    case "Archive":
                        return Check(await framework.ArchiveAsync(c.CompanyId, budget).ConfigureAwait(true), "Budget archived.");
                    default:
                        return Check(await framework.LockAsync(c.CompanyId, budget).ConfigureAwait(true), "Budget locked.");
                }
            },
            List = async c => Table(await c.Db.Set<Budget>().AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Page(c, 100).ToListAsync().ConfigureAwait(true),
                ("Id", b => b.Id), ("Label", b => b.Label), ("Type", b => b.Type), ("Version", b => b.VersionNumber), ("Status", b => b.Status), ("Department", b => b.Department),
                ("Rolled from", b => b.RolledFromBudgetId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Settle VAT for a period",
            Description = "Clears the period's output and input VAT accounts into the VAT payable (or refundable) account and shows output, input and the net.",
            Fields = [Today("From", "From"), Today("To", "To"), Req("Payable", "VAT payable account"), Req("Document", "Document number")],
            Run = async (c, i) =>
            {
                VatSettlementResult vat = Check(await c.Get<IVatReturnService>().SettleAsync(new SettleVatRequest
                {
                    CompanyId = c.CompanyId, FromDate = i.Date("From"), ToDate = i.Date("To"), VatPayableAccountCode = i.Text("Payable"), DocumentNumber = i.Text("Document"),
                }).ConfigureAwait(true));
                return $"Output {M(vat.OutputTax)} − input {M(vat.InputTax)} = {(vat.NetPayable >= 0m ? "payable" : "refundable")} {M(Math.Abs(vat.NetPayable))}{(vat.JournalNumber is null ? string.Empty : $" ({vat.JournalNumber})")}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Revalue foreign currency balances",
            Description = "Revalues the foreign-currency balances (banks, debtors, creditors, loans) at the closing rate on the date, posting the differences to FX gain / loss and, "
                + "if ticked, reversing them on the next day.",
            Fields = [Today("Date", "As at"), Tick("Reverse", "Reverse next day", true)],
            Run = async (c, i) =>
            {
                FxRevaluationResult fx = Check(await c.Get<IFxRevaluationService>().RevalueAsync(c.CompanyId, i.Date("Date"), i.Flag("Reverse")).ConfigureAwait(true));
                return fx.Accounts.Count == 0 ? "No foreign-currency balances to revalue."
                    : string.Join(Environment.NewLine, fx.Accounts.Select(a => $"{a.AccountCode,-8} {a.Currency} {Q(a.ForeignBalance),14} @ {a.ClosingRate:0.######}: {M(a.BaseBefore)} → {M(a.BaseAfter)}"))
                        + Environment.NewLine + $"Net difference {M(fx.NetDifference)}; {string.Join(", ", fx.JournalNumbers)}{(fx.ReversalJournalNumbers.Count == 0 ? string.Empty : $", reversed by {string.Join(", ", fx.ReversalJournalNumbers)}")}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Accruals and prepayments",
            Description = "An accrual (an expense incurred, not yet invoiced) or a prepayment (paid ahead), posted on the date and reversed automatically on the reversal date.",
            Fields = [Pick("Kind", "Kind", ["Accrual", "Prepayment"]), Today("Date", "Date"), Today("Reversal", "Reverses on"), Req("Expense", "Expense account"), Req("Balance", "Accrual / prepayment account"),
                Num("Amount", "Amount"), Req("Document", "Document number"), Req("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                IAccrualService accruals = c.Get<IAccrualService>();
                ReversingJournalResult posted = i.Text("Kind") == "Accrual"
                    ? Check(await accruals.PostAccrualAsync(c.CompanyId, i.Date("Date"), i.Date("Reversal"), i.Text("Expense"), i.Text("Balance"), i.Number("Amount"), i.Text("Document"), i.Text("Narrative")).ConfigureAwait(true))
                    : Check(await accruals.PostPrepaymentAsync(c.CompanyId, i.Date("Date"), i.Date("Reversal"), i.Text("Balance"), i.Text("Expense"), i.Number("Amount"), i.Text("Document"), i.Text("Narrative")).ConfigureAwait(true));
                return $"Posted {posted.PostingJournalNumber}; reverses as {posted.ReversalJournalNumber}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Recurring journals", ListTitle = "Recurring journals",
            Description = "Create: a journal posted again each period (\"6100 Dr 500 cc=ADM; 2100 Cr 500\"). Post: one of them on a date. Post all: every active one. Stop / restart one.",
            Fields = [Pick("Step", "Step", ["Create", "Post", "Post all", "Stop", "Restart"]), Opt("Code", "Code"), Opt("Description", "Description"), Opt("Lines", "Lines (create)"),
                Opt("Narrative", "Narrative"), Opt("Document", "Document number (post)"), Today()],
            Run = async (c, i) =>
            {
                IRecurringJournalService recurring = c.Get<IRecurringJournalService>();
                async Task<long> Id() => await FindAsync(c.Db.Set<Core.Accounting.RecurringJournal>(), r => r.CompanyId == c.CompanyId && r.Code == (i.Optional("Code") ?? string.Empty).ToUpper(), r => r.Id,
                    "recurring journal", i.Optional("Code") ?? string.Empty).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Create":
                        List<RecurringJournalLineInput> lines = [.. JournalLines(i.Optional("Lines") ?? string.Empty)
                            .Select(l => new RecurringJournalLineInput(l.AccountCode, l.Side == JournalSide.Debit, l.TransactionAmount, l.CostCentre, l.Department))];
                        return Check(await recurring.CreateAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Optional("Description") ?? string.Empty, "GL", lines, i.Optional("Narrative")).ConfigureAwait(true),
                            _ => "Recurring journal created.");
                    case "Post":
                        return $"Posted {Check(await recurring.PostAsync(c.CompanyId, await Id().ConfigureAwait(true), i.Date("Date"), i.Optional("Document")).ConfigureAwait(true))}.";
                    case "Post all":
                        IReadOnlyList<RecurringRunLine> run = Check(await recurring.PostAllActiveAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                        return run.Count == 0 ? "Nothing to post." : string.Join(Environment.NewLine, run.Select(r => $"{r.Code}: {r.JournalNumber}{(r.AlreadyPosted ? " (already posted)" : string.Empty)}"));
                    default:
                        bool active = i.Text("Step") == "Restart";
                        return Check(await recurring.SetActiveAsync(c.CompanyId, await Id().ConfigureAwait(true), active).ConfigureAwait(true), active ? "Restarted." : "Stopped.");
                }
            },
            List = async c => Table(await c.Db.Set<Core.Accounting.RecurringJournal>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.Code).ToListAsync().ConfigureAwait(true),
                ("Code", r => r.Code), ("Description", r => r.Description), ("Active", r => r.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Recognise deferred income",
            Description = "Recognises the income earned by the date on every deferred income schedule (fees billed in advance, annual licences) and shows each schedule's share.",
            Fields = [Today("Date", "As at")],
            Run = async (c, i) =>
            {
                DeferredIncomeRunResult run = Check(await c.Get<IDeferredIncomeService>().RecogniseAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                return run.Lines.Count == 0 ? "No deferred income to recognise."
                    : string.Join(Environment.NewLine, run.Lines.Select(l => $"{l.Reference,-14} {l.Description,-30} {M(l.Amount),12}  earned {M(l.EarnedToDate),12}  now {M(l.RecognisedThisRun),12}"))
                        + Environment.NewLine + $"Recognised {M(run.Recognised)}{(run.JournalNumber is null ? string.Empty : $" ({run.JournalNumber})")}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Petty cash",
            Description = "Disburse: a petty cash payment to an expense account. Top up: reimburses the float back to its fixed amount from the bank (imprest).",
            Fields = [Pick("Step", "Step", ["Disburse", "Top up float"]), Num("Amount", "Amount / float amount"), Req("PettyCash", "Petty cash account"), Opt("Expense", "Expense account (disburse)"),
                Opt("Bank", "Bank account (top up)"), Req("Document", "Document number"), Today()],
            Run = async (c, i) =>
            {
                IPettyCashService petty = c.Get<IPettyCashService>();
                if (i.Text("Step") == "Disburse")
                {
                    return $"Paid ({Check(await petty.DisburseAsync(c.CompanyId, i.Number("Amount"), i.Date("Date"), i.Optional("Expense") ?? string.Empty, i.Text("PettyCash"), i.Text("Document")).ConfigureAwait(true))}).";
                }

                ImprestResult topped = Check(await petty.ReimburseToFloatAsync(c.CompanyId, i.Date("Date"), i.Text("PettyCash"), i.Optional("Bank") ?? string.Empty, i.Number("Amount"), i.Text("Document")).ConfigureAwait(true));
                return $"Reimbursed {M(topped.ReimbursedAmount)}; float now {M(topped.FloatBalanceAfter)}{(topped.JournalNumber is null ? string.Empty : $" ({topped.JournalNumber})")}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Petty cash funds",
            Description = "Imprest funds: Save fund (code, float, petty cash and bank accounts, over/short account, custodian); Pay out from a fund, claiming the recoverable input tax on the receipt "
                + "when a tax code is given; Top up reimburses the fund to its float; Count records the cash counted against the book; Reconcile posts the count's variance and tops the float up; List shows every fund.",
            Fields = [Pick("Step", "Step", ["List", "Save fund", "Pay out", "Top up", "Count", "Reconcile"]), Req("Fund", "Fund code"), Opt("Name", "Fund name (save)"), OptNum("Amount", "Float / amount paid / cash counted"),
                Opt("PettyCash", "Petty cash account (save)"), Opt("Bank", "Bank account (save)"), Opt("OverShort", "Over/short account (save)"), Opt("Custodian", "Custodian username (save)"),
                Opt("Expense", "Expense account (pay out)"), Opt("TaxCode", "Tax code on the receipt (pay out)"), Opt("Payee", "Payee (pay out)"), Opt("Narrative", "Narrative / note"),
                Opt("Denominations", "Denominations counted, e.g. 100x2, 20x5 (count)"), OptNum("CountId", "Count id (reconcile)"), Opt("Document", "Document number"), Today()],
            Run = async (c, i) =>
            {
                IPettyCashService petty = c.Get<IPettyCashService>();
                string document = i.Optional("Document") ?? $"PC-{i.Date("Date"):yyyyMMdd}";
                switch (i.Text("Step"))
                {
                    case "Save fund":
                        PettyCashFund saved = Check(await petty.SaveFundAsync(c.CompanyId, new PettyCashFundInput(i.Text("Fund"), i.Optional("Name") ?? i.Text("Fund"), i.Optional("PettyCash") ?? string.Empty,
                            i.Optional("Bank") ?? string.Empty, i.OptionalNumber("Amount") ?? 0m, i.Optional("OverShort"), i.Optional("Custodian"))).ConfigureAwait(true));
                        return $"Fund {saved.Code} saved: float {M(saved.FloatAmount)} in {saved.AccountCode}, reimbursed from {saved.BankAccountCode}.";
                    case "Pay out":
                        PettyCashDisbursementResult paid = Check(await petty.DisburseFromFundAsync(c.CompanyId, new PettyCashDisbursement(i.Text("Fund"), i.OptionalNumber("Amount") ?? 0m, i.Date("Date"),
                            i.Optional("Expense") ?? string.Empty, document, i.Optional("TaxCode"), i.Optional("Payee"), i.Optional("Narrative"))).ConfigureAwait(true));
                        return $"Paid ({paid.JournalNumber}): expense {M(paid.Net)}, input tax {M(paid.InputTax)}; fund now {M(paid.FundBalanceAfter)}.";
                    case "Top up":
                        ImprestResult topped = Check(await petty.ReimburseFundAsync(c.CompanyId, i.Text("Fund"), i.Date("Date"), document).ConfigureAwait(true));
                        return $"Reimbursed {M(topped.ReimbursedAmount)}; float now {M(topped.FloatBalanceAfter)}{(topped.JournalNumber is null ? string.Empty : $" ({topped.JournalNumber})")}.";
                    case "Count":
                        PettyCashCountResult counted = Check(await petty.CountAsync(c.CompanyId, new PettyCashCountInput(i.Text("Fund"), i.Date("Date"), i.OptionalNumber("Amount") ?? 0m, c.UserId,
                            i.Optional("Denominations"), i.Optional("Narrative"))).ConfigureAwait(true));
                        return $"Count {counted.CountId}: book {M(counted.BookBalance)}, counted {M(counted.CountedAmount)}, variance {M(counted.Variance)}. Reconcile with this count id.";
                    case "Reconcile":
                        PettyCashReconciliation rec = Check(await petty.ReconcileAsync(c.CompanyId, (long)(i.OptionalNumber("CountId") ?? 0m), i.Date("Date"), document, c.UserId).ConfigureAwait(true));
                        return $"Variance {M(rec.Variance)}{(rec.VarianceJournalNumber is null ? string.Empty : $" ({rec.VarianceJournalNumber})")}; reimbursed {M(rec.ReimbursedAmount)}"
                            + $"{(rec.ReimbursementJournalNumber is null ? string.Empty : $" ({rec.ReimbursementJournalNumber})")}; float now {M(rec.FloatBalanceAfter)}.";
                    default:
                        IReadOnlyList<PettyCashFundRow> funds = await petty.ListFundsAsync(c.CompanyId).ConfigureAwait(true);
                        return funds.Count == 0 ? "No petty cash funds."
                            : string.Join(Environment.NewLine, funds.Select(f => $"{f.Code,-10} {f.Name,-24} {f.AccountCode,-8} float {M(f.FloatAmount),10} book {M(f.Balance),10}  {f.Custodian ?? "—",-12} {(f.IsActive ? "open" : "closed")}"
                                + (f.LastCountDate is null ? string.Empty : $"  last count {f.LastCountDate:yyyy-MM-dd}")));
                }
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Import journals from a file",
            Description = "Imports manual journals from a CSV file or an Excel workbook (its first sheet), all or nothing (a file with any problem imports nothing); journals over "
                + "the approval threshold wait for a second user's approval instead of posting.",
            Fields = [Req("File", "CSV or .xlsx file path")],
            Run = async (c, i) =>
            {
                byte[] file = await System.IO.File.ReadAllBytesAsync(i.Text("File")).ConfigureAwait(true);
                string csv = Application.Onboarding.XlsxReader.IsWorkbook(file) ? Application.Onboarding.XlsxReader.ToCsv(file) : System.Text.Encoding.UTF8.GetString(file);
                IReadOnlyList<ImportedJournalResult> imported = Check(await c.Get<IJournalImportService>().ImportAsync(c.CompanyId, csv, c.UserId).ConfigureAwait(true));
                return string.Join(Environment.NewLine, imported.Select(j => $"{j.Reference,-14} {j.JournalNumber}{(j.AwaitingApproval ? " (awaiting approval)" : string.Empty)}"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Overhead allocation", ListTitle = "Allocation rules",
            Description = "Rules that recharge a cost centre's overheads to others by fixed %, headcount, floor area or a usage driver (targets \"CC1:60; CC2:40\", optionally CC:driver:dept). "
                + "Drivers updates a rule's targets; Preview shows the period's recharge; Run posts it.",
            Fields = [Pick("Step", "Step", ["Create rule", "Drivers", "Preview", "Run"]), Opt("Code", "Rule code"), Opt("Name", "Name"), Num("Sequence", "Order", "1"),
                Pick("Basis", "Basis", Enum.GetNames<Core.Accounting.AllocationBasis>()), Opt("Source", "Source account"), Opt("SourceCc", "Source cost centre"), Opt("Target", "Target account (blank: same)"),
                Opt("Targets", "Targets: CC:driver[:dept]; …"), Today("Date", "Period (any day in it)")],
            Run = async (c, i) =>
            {
                IOverheadAllocationService allocation = c.Get<IOverheadAllocationService>();
                List<AllocationTargetRequest> Targets()
                {
                    List<AllocationTargetRequest> targets = [];
                    foreach (string part in (i.Optional("Targets") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        targets.Add(bits.Length is 2 or 3 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal driver)
                            ? new AllocationTargetRequest(bits[0], bits.Length == 3 ? bits[2] : null, driver) : throw new OperationException($"Write each target as CC:driver[:dept] (\"{part}\")."));
                    }

                    return targets;
                }

                static string Show(AllocationRunResult run) => run.Rules.Count == 0 ? "No rules." : string.Join(Environment.NewLine, run.Rules.Select(r =>
                    $"{r.RuleCode} {r.RuleName} ({r.Basis}): pool {M(r.Pool)}{(r.JournalNumber is null ? string.Empty : $" → {r.JournalNumber}")}" + Environment.NewLine
                    + string.Join(Environment.NewLine, r.Shares.Select(x => $"    {x.CostCentre,-8} {x.Department,-6} driver {Q(x.Driver),10}  {M(x.Amount),12}"))));
                switch (i.Text("Step"))
                {
                    case "Create rule":
                        return Check(await allocation.CreateRuleAsync(new AllocationRuleRequest
                        {
                            CompanyId = c.CompanyId, Code = i.Optional("Code") ?? string.Empty, Name = i.Optional("Name") ?? string.Empty, Sequence = i.Whole("Sequence"),
                            Basis = Enum.Parse<Core.Accounting.AllocationBasis>(i.Text("Basis")), SourceAccountCode = i.Optional("Source") ?? string.Empty, SourceCostCentre = i.Optional("SourceCc") ?? string.Empty,
                            TargetAccountCode = i.Optional("Target"), Targets = Targets(),
                        }).ConfigureAwait(true), id => $"Rule {id} created.");
                    case "Drivers":
                        long rule = await FindAsync(c.Db.Set<Core.Accounting.AllocationRule>(), r => r.CompanyId == c.CompanyId && r.Code == (i.Optional("Code") ?? string.Empty).ToUpper(), r => r.Id,
                            "allocation rule", i.Optional("Code") ?? string.Empty).ConfigureAwait(true);
                        return Check(await allocation.SetDriversAsync(c.CompanyId, rule, Targets()).ConfigureAwait(true), "Drivers updated.");
                    case "Preview":
                        return Show(Check(await allocation.PreviewAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true)));
                    default:
                        return Show(Check(await allocation.RunAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true)));
                }
            },
            List = async c => Table(await c.Db.Set<Core.Accounting.AllocationRule>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.Sequence).ToListAsync().ConfigureAwait(true),
                ("Code", r => r.Code), ("Name", r => r.Name), ("Order", r => r.Sequence), ("Basis", r => r.Basis), ("Source", r => r.SourceAccountCode), ("Cost centre", r => r.SourceCostCentre)),
        };
        yield return new Operation
        {
            Module = module, Title = "Intercompany journal",
            Description = "Posts both sides of an intercompany charge at once: in this company against its intercompany account, and in the other company against its own.",
            Fields = [Req("ToCompany", "Other company code"), Num("Amount", "Amount"), Req("Document", "Document number"), Opt("Narrative", "Narrative"), Req("FromAccount", "This company's account"),
                Req("FromIc", "This company's intercompany account"), Req("ToAccount", "Other company's account"), Req("ToIc", "Other company's intercompany account"), Today()],
            Run = async (c, i) =>
            {
                long other = await FindAsync(c.Db.Companies, x => x.Code == i.Text("ToCompany").ToUpper(), x => x.Id, "company", i.Text("ToCompany")).ConfigureAwait(true);
                IntercompanyPostingResult posted = Check(await c.Get<IIntercompanyService>().PostAsync(new IntercompanyJournalRequest
                {
                    FromCompanyId = c.CompanyId, ToCompanyId = other, Amount = i.Number("Amount"), Date = i.Date("Date"), DocumentNumber = i.Text("Document"), Narrative = i.Optional("Narrative"),
                    FromAccountCode = i.Text("FromAccount"), FromIntercompanyAccountCode = i.Text("FromIc"), ToAccountCode = i.Text("ToAccount"), ToIntercompanyAccountCode = i.Text("ToIc"),
                }).ConfigureAwait(true));
                return $"Posted {posted.FromJournalNumber} here and {posted.ToJournalNumber} in {i.Text("ToCompany").ToUpperInvariant()}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Hyperinflation (IAS 29)",
            Description = "Index: records a price index value for a month. Restate: the trial balance restated in current terms by the index — non-monetary items moved by the index "
                + "change since they arose — with the gain or loss on the net monetary position.",
            Fields = [Pick("Step", "Step", ["Index", "Restate"]), Req("Index", "Index code", "CPI"), Today("Date", "Month / as at"), Num("Value", "Index value (index)", "0")],
            Run = async (c, i) =>
            {
                IHyperinflationService inflation = c.Get<IHyperinflationService>();
                if (i.Text("Step") == "Index")
                {
                    return Check(await inflation.SetIndexAsync(c.CompanyId, i.Text("Index"), i.Date("Date"), i.Number("Value")).ConfigureAwait(true), "Index value saved.");
                }

                RestatedTrialBalance tb = Check(await inflation.RestatedTrialBalanceAsync(c.CompanyId, i.Date("Date"), i.Text("Index")).ConfigureAwait(true));
                return $"Closing index {tb.ClosingIndex:0.####}" + Environment.NewLine + string.Join(Environment.NewLine, tb.Rows.Select(r =>
                    $"{r.AccountCode,-8} {r.Name,-28} {(r.Monetary ? "monetary" : "non-monetary"),-12} {M(r.Historical),14} → {M(r.Restated),14}"))
                    + Environment.NewLine + $"Gain / loss on the net monetary position: {M(tb.MonetaryGainLoss)}.";
            },
        };
    }

    // "6100 Dr 500 project=P1 fund=WATER # narrative; 2410 Cr 500 fund=WATER"
    private static List<JournalLineRequest> JournalLines(string text)
    {
        List<JournalLineRequest> lines = [];
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] narrativeSplit = part.Split('#', 2, StringSplitOptions.TrimEntries);
            string[] words = narrativeSplit[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 3 || !decimal.TryParse(words[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
                || words[1].ToUpperInvariant() is not ("DR" or "CR"))
            {
                throw new OperationException($"\"{part}\" should be ACCOUNT Dr|Cr AMOUNT [cc= dept= project= fund= branch=] [# narrative].");
            }

            Dictionary<string, string> tags = words.Skip(3).Select(w => w.Split('=', 2)).Where(kv => kv.Length == 2)
                .ToDictionary(kv => kv[0].ToLowerInvariant(), kv => kv[1], StringComparer.OrdinalIgnoreCase);
            lines.Add(new JournalLineRequest
            {
                AccountCode = words[0], Side = words[1].Equals("dr", StringComparison.OrdinalIgnoreCase) ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = amount,
                CostCentre = tags.GetValueOrDefault("cc"), Department = tags.GetValueOrDefault("dept"), Project = tags.GetValueOrDefault("project"), Fund = tags.GetValueOrDefault("fund"),
                Branch = tags.GetValueOrDefault("branch"), Narrative = narrativeSplit.Length > 1 ? narrativeSplit[1] : null,
            });
        }

        return lines.Count >= 2 ? lines : throw new OperationException("A journal needs at least two lines.");
    }
}
