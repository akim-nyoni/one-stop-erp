using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>The multi-year framework, roll-forward, archiving and locking (brief §4.18).</summary>
public sealed class BudgetFrameworkService : IBudgetFrameworkService
{
    private const int MaxOuterYears = 4;
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BudgetFrameworkService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetOuterYearsAsync(long companyId, long budgetId, IReadOnlyList<OuterYearAmount> amounts, CancellationToken ct = default)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Result.Failure(NotFound(budgetId));
        }

        if (budget.IsSealed || budget.IsPutAway)
        {
            return Result.Failure(Error.Rule("Budget.FrameworkSealed", $"Budget {budget.Label} v{budget.VersionNumber} is approved; roll it forward or create a new version."));
        }

        if (amounts.Any(a => a.YearOffset is < 1 or > MaxOuterYears || string.IsNullOrWhiteSpace(a.AccountCode))
            || amounts.GroupBy(a => (a.YearOffset, a.AccountCode.Trim(), a.CostCentre)).Any(g => g.Count() > 1))
        {
            return Result.Failure(Error.Validation("Budget.OuterYears", $"Outer years run 1 to {MaxOuterYears} after the budget year, each account once a year."));
        }

        budget.SetOuterYears(amounts.Select(a => new BudgetOuterYearLine(a.YearOffset, a.AccountCode.Trim(), a.CostCentre, a.Amount)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FrameworkRow>>> FrameworkAsync(long companyId, long budgetId, CancellationToken ct = default)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return NotFound(budgetId);
        }

        int span = budget.OuterYears.Count == 0 ? 0 : budget.OuterYears.Max(o => o.YearOffset);
        var keys = budget.Lines.Where(l => l.VirementId is null && l.RephaseId is null).Select(l => (l.AccountCode, l.CostCentre))
            .Concat(budget.OuterYears.Select(o => (o.AccountCode, o.CostCentre))).Distinct()
            .OrderBy(k => k.AccountCode, StringComparer.Ordinal).ThenBy(k => k.CostCentre, StringComparer.Ordinal);
        List<FrameworkRow> rows = [];
        foreach ((string account, string? costCentre) in keys)
        {
            List<decimal> years = [budget.Lines.Where(l => l.AccountCode == account && l.CostCentre == costCentre).Sum(l => l.Amount)];
            for (int offset = 1; offset <= span; offset++)
            {
                years.Add(budget.OuterYears.Where(o => o.YearOffset == offset && o.AccountCode == account && o.CostCentre == costCentre).Sum(o => o.Amount));
            }

            rows.Add(new FrameworkRow(account, costCentre, years));
        }

        return rows;
    }

    /// <inheritdoc />
    public Task<Result<long>> RollForwardAsync(long companyId, long budgetId, long nextFiscalYearId, string label, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Budget? source = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
            if (source is null)
            {
                return NotFound(budgetId);
            }

            if (!source.IsSealed || source.OuterYears.Count == 0)
            {
                return Error.Rule("Budget.RollForward", "Only an approved budget with outer years can be rolled forward.");
            }

            if (nextFiscalYearId == source.FiscalYearId || !await _db.FiscalYears.AnyAsync(y => y.Id == nextFiscalYearId && y.CompanyId == companyId, ct).ConfigureAwait(false))
            {
                return Error.Validation("Budget.RollForwardYear", "Roll forward into a different fiscal year of the company.");
            }

            if (await _db.Set<Budget>().AnyAsync(b => b.RolledFromBudgetId == source.Id && b.FiscalYearId == nextFiscalYearId, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Budget.RolledAlready", $"Budget {source.Label} v{source.VersionNumber} has already been rolled into that year.");
            }

            int version = await _db.Set<Budget>().Where(b => b.CompanyId == companyId && b.FiscalYearId == nextFiscalYearId && b.Type == source.Type && b.Department == source.Department)
                .Select(b => (int?)b.VersionNumber).MaxAsync(ct).ConfigureAwait(false) ?? 0;
            Budget next = new(companyId, nextFiscalYearId, string.IsNullOrWhiteSpace(label) ? source.Label : label.Trim(), source.Type, source.Department, version + 1);
            next.MarkRolledFrom(source.Id);

            // The first outer year becomes the budget year, spread evenly with the rounding in the last period.
            foreach (BudgetOuterYearLine year in source.OuterYears.Where(o => o.YearOffset == 1))
            {
                decimal monthly = decimal.Round(year.Amount / 12m, 2, MidpointRounding.AwayFromZero);
                for (int period = 1; period <= 12; period++)
                {
                    next.AddLine(year.AccountCode, period, period < 12 ? monthly : year.Amount - (monthly * 11m), year.CostCentre);
                }
            }

            // The later outer years move up; the last one carries on so the framework keeps its length.
            int span = source.OuterYears.Max(o => o.YearOffset);
            List<BudgetOuterYearLine> outer = [.. source.OuterYears.Where(o => o.YearOffset > 1).Select(o => new BudgetOuterYearLine(o.YearOffset - 1, o.AccountCode, o.CostCentre, o.Amount))];
            outer.AddRange(source.OuterYears.Where(o => o.YearOffset == span).Select(o => new BudgetOuterYearLine(span, o.AccountCode, o.CostCentre, o.Amount)));
            next.SetOuterYears(outer);
            _db.Set<Budget>().Add(next);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<long>.Success(next.Id);
        }, ct);

    /// <inheritdoc />
    public Task<Result> ArchiveAsync(long companyId, long budgetId, CancellationToken ct = default) => PutAwayAsync(companyId, budgetId, b => b.Archive(), ct);

    /// <inheritdoc />
    public Task<Result> LockAsync(long companyId, long budgetId, CancellationToken ct = default) => PutAwayAsync(companyId, budgetId, b => b.Lock(), ct);

    private async Task<Result> PutAwayAsync(long companyId, long budgetId, Func<Budget, string?> step, CancellationToken ct)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Result.Failure(NotFound(budgetId));
        }

        string? refused = step(budget);
        if (refused is not null)
        {
            return Result.Failure(Error.Rule("Budget.PutAway", $"Budget {budget.Label} v{budget.VersionNumber}: {refused}"));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<Budget?> LoadAsync(long companyId, long budgetId, CancellationToken ct)
        => _db.Set<Budget>().Include(b => b.Lines).Include(b => b.OuterYears).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct);

    private static Error NotFound(long budgetId) => Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
}
