using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>
/// Budgeting, Planning &amp; Commitment Control (brief §4.18). Budgets are stored in the <c>bud</c> schema and
/// never post to the GL; actuals are read from the GL period balances and joined by account at report time.
/// Versions are retained permanently — activating a new version supersedes the previous active one.
/// </summary>
public sealed class BudgetingService : IBudgetingService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BudgetingService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> CreateBudgetAsync(long companyId, long fiscalYearId, string label, BudgetType type, string? department, IReadOnlyList<NewBudgetLine> lines, CancellationToken ct = default)
    {
        int lastVersion = await _db.Set<Budget>()
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.Type == type && b.Department == department)
            .Select(b => (int?)b.VersionNumber)
            .MaxAsync(ct).ConfigureAwait(false) ?? 0;

        foreach (NewBudgetLine line in lines)
        {
            if (line.Quantity is <= 0m)
            {
                return Error.Validation("Budget.LineQuantity", $"The budgeted quantity on {line.AccountCode} period {line.Period} must be positive.");
            }

            if (line.Currency is not null && line.PlannedRate is not > 0m)
            {
                return Error.Validation("Budget.PlannedRate", $"A line planned in {line.Currency} needs a positive planned rate.");
            }
        }

        Budget budget = new(companyId, fiscalYearId, label, type, department, lastVersion + 1);
        foreach (NewBudgetLine line in lines)
        {
            BudgetLine added = budget.AddLine(line.AccountCode, line.Period, line.Amount, line.CostCentre);
            added.Quantity = line.Quantity;
            added.Currency = line.Currency;
            added.PlannedRate = line.Currency is null ? null : line.PlannedRate;
        }

        _db.Set<Budget>().Add(budget);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return budget.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordActualVolumeAsync(long companyId, string accountCode, string? department, DateOnly date, decimal quantity, string reference, CancellationToken ct = default)
    {
        string account = (accountCode ?? string.Empty).Trim();
        if (!await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Budget.AccountNotFound", $"Account {account} not found.");
        }

        if (quantity == 0m)
        {
            return Error.Validation("Budget.VolumeZero", "An actual volume cannot be zero.");
        }

        ActualVolume volume = new(companyId, account, string.IsNullOrWhiteSpace(department) ? null : department.Trim(), date, quantity, (reference ?? string.Empty).Trim());
        _db.Set<ActualVolume>().Add(volume);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return volume.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ActivateAsync(long companyId, long budgetId, CancellationToken ct = default)
    {
        Budget? budget = await _db.Set<Budget>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Result.Failure(Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found."));
        }

        List<Budget> priorActive = await _db.Set<Budget>()
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == budget.FiscalYearId && b.Type == budget.Type && b.Department == budget.Department && b.IsActive && b.Id != budgetId)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (Budget prior in priorActive)
        {
            prior.Supersede();
        }

        budget.Activate();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<BudgetVsActualReport>> BudgetVsActualAsync(long companyId, long fiscalYearId, BudgetType type, string? department, int asAtPeriod, CancellationToken ct = default)
    {
        Budget? budget = await _db.Set<Budget>().Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.Type == type && b.Department == department && b.IsActive, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NoActiveVersion", "There is no active budget for the given year/type/department.");
        }

        Dictionary<string, decimal> budgetByAccount = budget.Lines
            .Where(l => l.Period <= asAtPeriod)
            .GroupBy(l => l.AccountCode)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        Dictionary<string, decimal> actualByAccount = await ActualsByAccountAsync(companyId, fiscalYearId, asAtPeriod, ct).ConfigureAwait(false);

        List<BudgetVsActualRow> rows = budgetByAccount.Keys.Union(actualByAccount.Keys)
            .OrderBy(a => a)
            .Select(account => new BudgetVsActualRow(account, budgetByAccount.GetValueOrDefault(account), actualByAccount.GetValueOrDefault(account)))
            .ToList();

        return new BudgetVsActualReport(fiscalYearId, rows);
    }

    /// <inheritdoc />
    public async Task<Result<AvailabilityResult>> CheckAvailabilityAsync(long companyId, long fiscalYearId, BudgetType type, string? department, string accountCode, decimal proposedAmount, int asAtPeriod, CancellationToken ct = default)
    {
        Budget? budget = await _db.Set<Budget>().Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.Type == type && b.Department == department && b.IsActive, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NoActiveVersion", "There is no active budget for the given year/type/department.");
        }

        decimal budgetAmount = budget.Lines.Where(l => l.AccountCode == accountCode && l.Period <= asAtPeriod).Sum(l => l.Amount);
        Dictionary<string, decimal> actuals = await ActualsByAccountAsync(companyId, fiscalYearId, asAtPeriod, ct).ConfigureAwait(false);
        decimal actual = actuals.GetValueOrDefault(accountCode);

        decimal committed = await _db.Set<BudgetCommitment>()
            .Where(c => c.CompanyId == companyId && c.FiscalYearId == fiscalYearId && c.Type == type && c.Department == department
                && c.AccountCode == accountCode && c.Period <= asAtPeriod && c.Status == CommitmentStatus.Open)
            .SumAsync(c => (decimal?)c.Amount, ct).ConfigureAwait(false) ?? 0m;

        decimal available = budgetAmount - actual - committed;
        return new AvailabilityResult(accountCode, budgetAmount, actual, committed, available, proposedAmount, proposedAmount <= available);
    }

    /// <inheritdoc />
    public async Task<Result<long>> CommitAsync(long companyId, long fiscalYearId, BudgetType type, string? department, string accountCode, int period, decimal amount, string reference, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Validation("Budget.Commit.Amount", "A commitment amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(accountCode))
        {
            return Error.Validation("Budget.Commit.Account", "A commitment needs an account.");
        }

        BudgetCommitment commitment = new(companyId, fiscalYearId, type, department, accountCode.Trim(), period, amount, reference?.Trim() ?? string.Empty);
        _db.Set<BudgetCommitment>().Add(commitment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return commitment.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ReleaseCommitmentAsync(long companyId, long commitmentId, CancellationToken ct = default)
    {
        BudgetCommitment? commitment = await _db.Set<BudgetCommitment>()
            .FirstOrDefaultAsync(c => c.Id == commitmentId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result.Failure(Error.NotFound("Budget.Commit.NotFound", $"No commitment {commitmentId}."));
        }

        if (!commitment.Release())
        {
            return Result.Failure(Error.Rule("Budget.Commit.AlreadyReleased", "The commitment is already released."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Dictionary<string, decimal>> ActualsByAccountAsync(long companyId, long fiscalYearId, int asAtPeriod, CancellationToken ct)
    {
        // Actual movement (debit-positive) per account for the year up to the as-at period, read from the
        // GL period balances — budgets never post, so this is the only place actuals come from.
        var query =
            from bal in _db.Set<GlPeriodBalance>()
            join period in _db.Set<FiscalPeriod>() on bal.FiscalPeriodId equals period.Id
            where bal.CompanyId == companyId && bal.FiscalYearId == fiscalYearId && period.PeriodNumber <= asAtPeriod
            group new { bal.BaseDebit, bal.BaseCredit } by bal.AccountCode into g
            select new { AccountCode = g.Key, Movement = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) };

        return await query.ToDictionaryAsync(x => x.AccountCode, x => x.Movement, ct).ConfigureAwait(false);
    }
}
