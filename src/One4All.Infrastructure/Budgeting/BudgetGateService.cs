using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>The budget check at the spending gates, commitments and reservations (brief §4.18.5).</summary>
public sealed class BudgetGateService : IBudgetGateService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BudgetGateService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetRuleAsync(long companyId, string? department, string? accountPrefix, decimal minimumValue, BudgetAction action, CancellationToken ct = default)
    {
        if (minimumValue < 0m)
        {
            return Result.Failure(Error.Validation("Budget.RuleInvalid", "A value band starts at zero or more."));
        }

        string? dept = Clean(department), prefix = Clean(accountPrefix);
        BudgetEnforcementRule? rule = await _db.BudgetEnforcementRules
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Department == dept && r.AccountPrefix == prefix && r.MinimumValue == minimumValue, ct).ConfigureAwait(false);
        if (rule is null)
        {
            _db.BudgetEnforcementRules.Add(new BudgetEnforcementRule(companyId, dept, prefix, minimumValue, action));
        }
        else
        {
            rule.Action = action;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<BudgetGateResult>> CheckAsync(long companyId, DateOnly date, IReadOnlyList<BudgetDemand> demands, CancellationToken ct = default)
    {
        FiscalYear? year = await YearAsync(companyId, date, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("Budget.NoFiscalYear", $"No fiscal year covers {date:yyyy-MM-dd}.");
        }

        List<BudgetEnforcementRule> rules = await _db.BudgetEnforcementRules.AsNoTracking().Where(r => r.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        List<BudgetGateLine> lines = [];
        foreach (var group in demands.Where(d => d.Amount > 0m).GroupBy(d => (Account: d.AccountCode.Trim().ToUpperInvariant(), Department: Clean(d.Department))))
        {
            decimal amount = group.Sum(d => d.Amount);
            decimal available = await AvailableAsync(companyId, year, group.Key.Account, group.Key.Department, ct).ConfigureAwait(false);
            lines.Add(new BudgetGateLine(group.Key.Account, group.Key.Department, amount, available, Math.Max(0m, amount - available),
                BudgetGate.ActionFor(rules, group.Key.Department, group.Key.Account, amount)));
        }

        return new BudgetGateResult(BudgetGate.Overall(lines.Select(l => (l.Shortfall, l.Action))), lines);
    }

    /// <inheritdoc />
    public async Task<Result<int>> CommitAsync(long companyId, string reference, DateOnly date, IReadOnlyList<BudgetDemand> demands, CancellationToken ct = default)
    {
        FiscalYear? year = await YearAsync(companyId, date, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("Budget.NoFiscalYear", $"No fiscal year covers {date:yyyy-MM-dd}.");
        }

        int period = year.Periods.FirstOrDefault(p => p.StartDate <= date && p.EndDate >= date)?.PeriodNumber ?? 1;
        int raised = 0;
        foreach (var group in demands.Where(d => d.Amount > 0m).GroupBy(d => (Account: d.AccountCode.Trim().ToUpperInvariant(), Department: Clean(d.Department))))
        {
            _db.Add(new BudgetCommitment(companyId, year.Id, BudgetType.OriginalApproved, group.Key.Department, group.Key.Account, period, group.Sum(d => d.Amount), reference.Trim()));
            raised++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return raised;
    }

    /// <inheritdoc />
    public async Task<decimal> ReleaseAsync(long companyId, string reference, CancellationToken ct = default)
    {
        string key = reference.Trim();
        List<BudgetCommitment> open = await _db.Set<BudgetCommitment>().Where(c => c.CompanyId == companyId && c.Reference == key && c.Status == CommitmentStatus.Open)
            .ToListAsync(ct).ConfigureAwait(false);
        open.ForEach(c => c.Release());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return open.Sum(c => c.Amount);
    }

    /// <inheritdoc />
    public async Task<Result<BudgetGateResult>> ReserveAsync(long companyId, string reference, DateOnly date, BudgetDemand demand, CancellationToken ct = default)
    {
        string key = (reference ?? string.Empty).Trim();
        if (key.Length == 0 || demand.Amount <= 0m)
        {
            return Error.Validation("Budget.ReservationInvalid", "A reservation needs a reference and a positive amount.");
        }

        if (await _db.Set<BudgetCommitment>().AnyAsync(c => c.CompanyId == companyId && c.Reference == key && c.Status == CommitmentStatus.Open, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Budget.ReservationExists", $"An open commitment {key} already exists.");
        }

        Result<BudgetGateResult> check = await CheckAsync(companyId, date, [demand], ct).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check;
        }

        if (check.Value.Outcome >= BudgetAction.RequireApproval)
        {
            return Error.Rule("Budget.Blocked", check.Value.Warning ?? "Over budget.");
        }

        Result<int> committed = await CommitAsync(companyId, key, date, [demand], ct).ConfigureAwait(false);
        return committed.IsFailure ? committed.Error : check;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private Task<FiscalYear?> YearAsync(long companyId, DateOnly date, CancellationToken ct)
        => _db.FiscalYears.AsNoTracking().Include(y => y.Periods).FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= date && y.EndDate >= date, ct);

    // Full-year approved budget (the revised version when active, else the original) less the year's actuals and open commitments.
    private async Task<decimal> AvailableAsync(long companyId, FiscalYear year, string account, string? department, CancellationToken ct)
    {
        List<Budget> active = await _db.Set<Budget>().AsNoTracking().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == year.Id && b.IsActive && b.Department == department
                && (b.Type == BudgetType.OriginalApproved || b.Type == BudgetType.SupplementaryRevised))
            .ToListAsync(ct).ConfigureAwait(false);
        Budget? approved = active.FirstOrDefault(b => b.Type == BudgetType.SupplementaryRevised) ?? active.FirstOrDefault();
        decimal budget = approved?.Lines.Where(l => l.AccountCode == account).Sum(l => l.Amount) ?? 0m;
        decimal actual = await _db.Journals.AsNoTracking()
            .Where(j => j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate >= year.StartDate && j.PostingDate <= year.EndDate)
            .SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == account && (department == null || l.Department == department))
            .SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;
        decimal committed = await _db.Set<BudgetCommitment>().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.FiscalYearId == year.Id && c.AccountCode == account && c.Department == department && c.Status == CommitmentStatus.Open)
            .SumAsync(c => (decimal?)c.Amount, ct).ConfigureAwait(false) ?? 0m;
        return budget - actual - committed;
    }
}
