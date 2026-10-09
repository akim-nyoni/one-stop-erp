using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Incremental budgets, the reforecast and the cash budget.</summary>
public sealed class BudgetMethodService : IBudgetMethodService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BudgetMethodService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<Result<int>> BuildIncrementalAsync(IncrementalBudgetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => BuildCoreAsync(request, ct), ct);
    }

    private async Task<Result<int>> BuildCoreAsync(IncrementalBudgetRequest request, CancellationToken ct)
    {
        Budget? budget = await _db.Set<Budget>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == request.BudgetId && b.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {request.BudgetId} not found.");
        }

        if (budget.IsSealed || budget.IsActive)
        {
            return Error.Rule("Budget.NotDraft", "Only a draft budget is built from a prior year.");
        }

        if (request.SourceFiscalYearId == budget.FiscalYearId || !await _db.Set<FiscalYear>().AnyAsync(y => y.Id == request.SourceFiscalYearId && y.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.Validation("Budget.SourceYear", "Choose another fiscal year of the company to build from.");
        }

        string prefix = (request.AccountPrefix ?? string.Empty).Trim();
        HashSet<string> plAccounts = [.. await _db.Accounts.AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId && (a.Type == AccountType.Income || a.Type == AccountType.Expense) && a.AccountCode.StartsWith(prefix))
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        List<(string Account, string? CostCentre, int Period, decimal Amount)> source;
        if (request.Basis == IncrementalBasis.PriorYearActual)
        {
            source = [.. (await (
                    from bal in _db.Set<GlPeriodBalance>()
                    join period in _db.Set<FiscalPeriod>() on bal.FiscalPeriodId equals period.Id
                    where bal.CompanyId == request.CompanyId && bal.FiscalYearId == request.SourceFiscalYearId && period.PeriodNumber >= 1 && period.PeriodNumber <= 12
                    group bal by new { bal.AccountCode, period.PeriodNumber } into g
                    select new { g.Key.AccountCode, g.Key.PeriodNumber, Movement = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) })
                .ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.AccountCode, (string?)null, x.PeriodNumber, x.Movement))];
        }
        else
        {
            Budget? prior = await _db.Set<Budget>().AsNoTracking().Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.CompanyId == request.CompanyId && b.FiscalYearId == request.SourceFiscalYearId && b.Type == budget.Type && b.Department == budget.Department && b.IsActive, ct)
                .ConfigureAwait(false);
            if (prior is null)
            {
                return Error.NotFound("Budget.NoPriorBudget", "The prior year has no active budget of this type and department.");
            }

            source = [.. prior.Lines.Select(l => (l.AccountCode, l.CostCentre, l.Period, l.Amount))];
        }

        HashSet<string> already = [.. budget.Lines.Select(l => l.AccountCode)];
        int added = 0;
        foreach (var line in source.Where(s => plAccounts.Contains(s.Account) && !already.Contains(s.Account))
                     .GroupBy(s => (s.Account, s.CostCentre, s.Period)).OrderBy(g => g.Key.Account, StringComparer.Ordinal).ThenBy(g => g.Key.Period))
        {
            decimal amount = decimal.Round(line.Sum(s => s.Amount) * (100m + request.UpliftPercent) / 100m, 2, MidpointRounding.AwayFromZero);
            if (amount != 0m)
            {
                budget.AddLine(line.Key.Account, line.Key.Period, amount, line.Key.CostCentre);
                added++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return added;
    }

    /// <inheritdoc />
    public Task<Result<long>> ReforecastAsync(long companyId, long budgetId, int asAtPeriod, string label, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (asAtPeriod is < 1 or > 11 || string.IsNullOrWhiteSpace(label))
            {
                return Error.Validation("Budget.Reforecast", "A reforecast needs a label and an as-at period from 1 to 11.");
            }

            Budget? budget = await _db.Set<Budget>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
            if (budget is null)
            {
                return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
            }

            HashSet<string> plAccounts = [.. await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && (a.Type == AccountType.Income || a.Type == AccountType.Expense))
                .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
            var actuals = await (
                    from bal in _db.Set<GlPeriodBalance>()
                    join period in _db.Set<FiscalPeriod>() on bal.FiscalPeriodId equals period.Id
                    where bal.CompanyId == companyId && bal.FiscalYearId == budget.FiscalYearId && period.PeriodNumber >= 1 && period.PeriodNumber <= asAtPeriod
                    group bal by new { bal.AccountCode, period.PeriodNumber } into g
                    select new { g.Key.AccountCode, g.Key.PeriodNumber, Movement = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) })
                .ToListAsync(ct).ConfigureAwait(false);

            int version = await _db.Set<Budget>().Where(b => b.CompanyId == companyId && b.FiscalYearId == budget.FiscalYearId && b.Type == BudgetType.Reforecast && b.Department == budget.Department)
                .Select(b => (int?)b.VersionNumber).MaxAsync(ct).ConfigureAwait(false) ?? 0;
            Budget reforecast = new(companyId, budget.FiscalYearId, label.Trim(), BudgetType.Reforecast, budget.Department, version + 1);
            foreach (var actual in actuals.Where(a => plAccounts.Contains(a.AccountCode) && a.Movement != 0m))
            {
                reforecast.AddLine(actual.AccountCode, actual.PeriodNumber, actual.Movement);
            }

            foreach (BudgetLine line in budget.Lines.Where(l => l.Period > asAtPeriod))
            {
                reforecast.AddLine(line.AccountCode, line.Period, line.Amount, line.CostCentre);
            }

            _db.Set<Budget>().Add(reforecast);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<long>.Success(reforecast.Id);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<(IReadOnlyList<CashBudgetRow> Months, decimal CarryOut)>> CashBudgetAsync(long companyId, long budgetId, IReadOnlyList<CashTiming> timings, decimal openingCash,
        CancellationToken ct = default)
    {
        if (timings.Select(CashBudget.Problem).FirstOrDefault(p => p is not null) is string problem)
        {
            return Error.Validation("Budget.CashTiming", problem);
        }

        Budget? budget = await _db.Set<Budget>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        IReadOnlyList<CashBudgetRow> months = CashBudget.Project(budget.Lines.Select(l => (l.AccountCode, l.Period, l.Amount)), timings, openingCash, out decimal carryOut);
        return (months, carryOut);
    }
}
