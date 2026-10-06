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
