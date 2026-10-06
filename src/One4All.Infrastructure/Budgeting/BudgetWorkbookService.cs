using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using One4All.Reporting.Engine;

namespace One4All.Infrastructure.Budgeting;

/// <summary>The budget's Excel round trip (brief §4.18).</summary>
public sealed class BudgetWorkbookService : IBudgetWorkbookService
{
    // Layout: row 1 identifies the budget, row 2 the columns, lines from row 3.
    private const string Marker = "One4All budget";
    private const int Periods = 12;
    private const int FirstPeriodColumn = 3;
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    public BudgetWorkbookService(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<(byte[] Workbook, string FileName)>> ExportAsync(long companyId, long budgetId, bool includeBudgetAccounts, CancellationToken ct = default)
    {
        Budget? budget = await _db.Set<Budget>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        Dictionary<(string Account, string? CostCentre), decimal[]> grid = Figures(budget);
        if (includeBudgetAccounts)
        {
            foreach (string account in await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.IsActive && a.BudgetEnabled).Select(a => a.AccountCode)
                         .ToListAsync(ct).ConfigureAwait(false))
            {
                if (!grid.Keys.Any(k => k.Account == account))
                {
                    grid[(account, null)] = new decimal[Periods];
                }
            }
        }

        Dictionary<string, string> names = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);
        List<IReadOnlyList<object?>> rows =
        [
            [Marker, budget.Id, $"{budget.Label} v{budget.VersionNumber}", budget.Department, budget.Status.ToString()],
            [.. new object?[] { "Account", "Name", "Cost centre" }.Concat(Enumerable.Range(1, Periods).Select(p => (object?)$"P{p}")).Append("Total")],
        ];
        string last = ColumnLetter(FirstPeriodColumn + Periods - 1);
        foreach (((string account, string? costCentre), decimal[] amounts) in grid.OrderBy(g => g.Key.Account, StringComparer.Ordinal).ThenBy(g => g.Key.CostCentre, StringComparer.Ordinal))
        {
            int excelRow = rows.Count + 1;
            rows.Add([.. new object?[] { account, names.GetValueOrDefault(account), costCentre }.Concat(amounts.Select(a => (object?)a))
                .Append(new XlsxFormula($"SUM({ColumnLetter(FirstPeriodColumn)}{excelRow}:{last}{excelRow})"))]);
        }

        string file = $"budget-{budget.Id}-{new string([.. budget.Label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')])}-v{budget.VersionNumber}.xlsx";
        return (XlsxGrid.Write("Budget", rows, boldRows: 2), file);
    }

    /// <inheritdoc />
    public Task<Result<BudgetWorkbookImport>> ImportAsync(long companyId, long budgetId, byte[] workbook, long userId, bool commit, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Budget? budget = await _db.Set<Budget>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
            if (budget is null)
            {
                return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
            }

            IReadOnlyList<IReadOnlyList<string?>> sheet;
            try
            {
                sheet = XlsxGrid.Read(workbook);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
            {
                return Error.Validation("Budget.WorkbookUnreadable", $"The file is not a readable Excel workbook: {ex.Message}");
            }

            if (sheet.Count < 2 || Cell(sheet[0], 0) != Marker || Cell(sheet[0], 1) != budget.Id.ToString(CultureInfo.InvariantCulture))
            {
                return Error.Validation("Budget.WorkbookMismatch", $"This workbook was not exported from budget {budget.Id} ({budget.Label} v{budget.VersionNumber}).");
            }

            if (!Enumerable.Range(1, Periods).All(p => string.Equals(Cell(sheet[1], FirstPeriodColumn + p - 1), $"P{p}", StringComparison.OrdinalIgnoreCase)))
            {
                return Error.Validation("Budget.WorkbookLayout", "The period columns P1 to P12 have been moved or renamed; export a fresh workbook.");
            }

            HashSet<string> accounts = [.. await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.IsActive).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
            HashSet<string> costCentres = [.. await _db.CostCentres.AsNoTracking().Where(c => c.CompanyId == companyId && c.IsActive).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false)];
            Dictionary<(string Account, string? CostCentre), decimal[]> current = Figures(budget);
            List<BudgetWorkbookProblem> problems = [];
            List<BudgetWorkbookChange> changes = [];
            HashSet<(string, string?)> seen = [];

            for (int r = 2; r < sheet.Count; r++)
            {
                IReadOnlyList<string?> row = sheet[r];
                int excelRow = r + 1;
                string? account = Cell(row, 0)?.ToUpperInvariant();
                if (account is null)
                {
                    if (Enumerable.Range(FirstPeriodColumn, Periods).Any(c => Cell(row, c) is string s && s != "0"))
                    {
                        problems.Add(new BudgetWorkbookProblem(excelRow, "Figures without an account."));
                    }

                    continue;
                }

                string? costCentre = Cell(row, 2)?.ToUpperInvariant();
                if (!accounts.Contains(account))
                {
                    problems.Add(new BudgetWorkbookProblem(excelRow, $"Account {account} does not exist or is inactive."));
                    continue;
                }

                if (costCentre is not null && !costCentres.Contains(costCentre))
                {
                    problems.Add(new BudgetWorkbookProblem(excelRow, $"Cost centre {costCentre} does not exist or is inactive."));
                    continue;
                }

                if (!seen.Add((account, costCentre)))
                {
                    problems.Add(new BudgetWorkbookProblem(excelRow, $"{account}{(costCentre is null ? string.Empty : $" / {costCentre}")} appears more than once."));
                    continue;
                }

                decimal[] before = current.GetValueOrDefault((account, costCentre)) ?? new decimal[Periods];
                for (int p = 1; p <= Periods; p++)
                {
                    string? text = Cell(row, FirstPeriodColumn + p - 1);
                    if (!decimal.TryParse(text ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out decimal after))
                    {
                        problems.Add(new BudgetWorkbookProblem(excelRow, $"P{p} is not a number: \"{text}\"."));
                        continue;
                    }

                    after = Math.Round(after, 2, MidpointRounding.AwayFromZero);
                    if (after < 0m)
                    {
                        problems.Add(new BudgetWorkbookProblem(excelRow, $"P{p} is negative."));
                    }
                    else if (after != before[p - 1])
                    {
                        changes.Add(new BudgetWorkbookChange(account, costCentre, p, before[p - 1], after));
                    }
                }
            }

            decimal totalBefore = current.Values.Sum(v => v.Sum());
            decimal totalAfter = totalBefore + changes.Sum(c => c.After - c.Before);
            bool apply = commit && problems.Count == 0 && changes.Count > 0;
            if (apply)
            {
                if (budget.Status is not (BudgetStatus.Draft or BudgetStatus.Returned) || budget.IsSealed)
                {
                    return Error.Rule("Budget.WrongStage", $"Budget {budget.Label} v{budget.VersionNumber} is {budget.Status}; figures are imported into a draft or returned budget.");
                }

                foreach (BudgetWorkbookChange change in changes)
                {
                    Set(budget, change.AccountCode, change.Period, change.CostCentre, change.After);
                }

                _db.Add(new BudgetReviewEvent(budget.Id, budget.Round, BudgetReviewAction.Revised, userId, _clock.UtcNow,
                    $"Imported from Excel: {changes.Count} figure(s), total {totalBefore:0.00} → {totalAfter:0.00}"));
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Result<BudgetWorkbookImport>.Success(new BudgetWorkbookImport(changes, problems, totalBefore, totalAfter, apply));
        }, ct);

    // Ordinary lines (not virements or re-phasings) summed into account × cost centre × period.
    private static Dictionary<(string Account, string? CostCentre), decimal[]> Figures(Budget budget)
    {
        Dictionary<(string, string?), decimal[]> grid = [];
        foreach (BudgetLine line in budget.Lines.Where(l => l.VirementId is null && l.RephaseId is null && l.Period is >= 1 and <= Periods))
        {
            if (!grid.TryGetValue((line.AccountCode, line.CostCentre), out decimal[]? amounts))
            {
                grid[(line.AccountCode, line.CostCentre)] = amounts = new decimal[Periods];
            }

            amounts[line.Period - 1] += line.Amount;
        }

        return grid;
    }

    private static void Set(Budget budget, string account, int period, string? costCentre, decimal amount)
    {
        List<BudgetLine> lines = [.. budget.Lines.Where(l => l.AccountCode == account && l.Period == period && l.CostCentre == costCentre && l.VirementId is null && l.RephaseId is null)];
        if (lines.Count == 0)
        {
            budget.AddLine(account, period, amount, costCentre);
            return;
        }

        lines[0].SetAmount(amount);
        lines.Skip(1).ToList().ForEach(l => l.SetAmount(0m));
    }

    private static string? Cell(IReadOnlyList<string?> row, int column)
        => column < row.Count && !string.IsNullOrWhiteSpace(row[column]) ? row[column]!.Trim() : null;

    private static string ColumnLetter(int zeroBased) => zeroBased < 26 ? ((char)('A' + zeroBased)).ToString() : $"{(char)('A' + (zeroBased / 26) - 1)}{(char)('A' + (zeroBased % 26))}";
}
