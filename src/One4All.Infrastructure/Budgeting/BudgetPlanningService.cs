using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Assumptions, spreading, the staff budget and re-phasing (brief §4.18.2, §4.18.6).</summary>
public sealed class BudgetPlanningService : IBudgetPlanningService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BudgetPlanningService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DriverImpact>>> SetDriverAsync(long companyId, long fiscalYearId, string code, decimal value, bool commit, string? name = null, string? unit = null,
        CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0)
        {
            return Error.Validation("Budget.DriverInvalid", "An assumption needs a code.");
        }

        BudgetDriver? driver = await _db.BudgetDrivers.FirstOrDefaultAsync(d => d.CompanyId == companyId && d.FiscalYearId == fiscalYearId && d.Code == key, ct).ConfigureAwait(false);
        List<Budget> budgets = await _db.Set<Budget>().Include(b => b.Lines).Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.SealHash == null)
            .ToListAsync(ct).ConfigureAwait(false);
        List<DriverImpact> impacts = [];
        foreach (Budget budget in budgets)
        {
            foreach (BudgetLine line in budget.Lines.Where(l => l.DriverCode == key && l.Quantity is not null))
            {
                decimal after = Math.Round(line.Quantity!.Value * value, 2, MidpointRounding.AwayFromZero);
                if (after != line.Amount)
                {
                    impacts.Add(new DriverImpact(budget.Id, line.AccountCode, line.Period, line.Amount, after));
                    if (commit)
                    {
                        line.SetAmount(after);
                        line.PlannedRate = value;
                    }
                }
            }
        }

        if (commit)
        {
            if (driver is null)
            {
                _db.BudgetDrivers.Add(new BudgetDriver(companyId, fiscalYearId, key, string.IsNullOrWhiteSpace(name) ? key : name.Trim(), unit?.Trim() ?? string.Empty, value));
            }
            else
            {
                driver.Value = value;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return impacts;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> SetDriverLineAsync(long companyId, long budgetId, string accountCode, int period, string driverCode, decimal quantity, string? costCentre = null,
        CancellationToken ct = default)
    {
        Result<Budget> budget = await OpenBudgetAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget.IsFailure)
        {
            return budget.Error;
        }

        string key = driverCode.Trim().ToUpperInvariant();
        BudgetDriver? driver = await _db.BudgetDrivers.AsNoTracking().FirstOrDefaultAsync(d => d.CompanyId == companyId && d.FiscalYearId == budget.Value.FiscalYearId && d.Code == key, ct)
            .ConfigureAwait(false);
        if (driver is null || quantity < 0m)
        {
            return Error.NotFound("Budget.DriverNotFound", $"No assumption {key} for the budget's year, or the quantity is negative.");
        }

        decimal amount = Math.Round(quantity * driver.Value, 2, MidpointRounding.AwayFromZero);
        BudgetLine line = Place(budget.Value, accountCode.Trim().ToUpperInvariant(), period, costCentre, amount);
        line.Basis = BudgetBasis.DriverBased;
        line.DriverCode = key;
        line.Quantity = quantity;
        line.PlannedRate = driver.Value;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return amount;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<decimal>>> SpreadAsync(long companyId, long budgetId, string accountCode, decimal annual, SpreadProfile profile, IReadOnlyList<decimal>? weights = null,
        string? costCentre = null, CancellationToken ct = default)
    {
        Result<Budget> budget = await OpenBudgetAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget.IsFailure)
        {
            return budget.Error;
        }

        FiscalYear year = await _db.FiscalYears.AsNoTracking().Include(y => y.Periods).SingleAsync(y => y.Id == budget.Value.FiscalYearId, ct).ConfigureAwait(false);
        List<FiscalPeriod> periods = [.. year.OrderedPeriods];
        string account = accountCode.Trim().ToUpperInvariant();
        IReadOnlyList<decimal> profileWeights;
        switch (profile)
        {
            case SpreadProfile.Custom when weights is null || weights.Count != periods.Count:
                return Error.Validation("Budget.WeightsInvalid", $"A custom spread needs one weight for each of the year's {periods.Count} periods.");
            case SpreadProfile.Custom:
                profileWeights = weights!;
                break;
            case SpreadProfile.SCurve:
                profileWeights = BudgetSpread.SCurve(periods.Count);
                break;
            case SpreadProfile.WorkingDays:
                profileWeights = BudgetSpread.WorkingDays([.. periods.Select(p => (p.StartDate, p.EndDate))]);
                break;
            case SpreadProfile.Seasonal:
                profileWeights = await PriorYearWeightsAsync(companyId, year, periods.Count, account, ct).ConfigureAwait(false);
                break;
            default:
                profileWeights = [.. periods.Select(_ => 1m)];
                break;
        }

        IReadOnlyList<decimal> amounts = BudgetSpread.ByWeights(annual, profileWeights);
        for (int i = 0; i < periods.Count; i++)
        {
            Place(budget.Value, account, periods[i].PeriodNumber, costCentre, amounts[i]);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<decimal>>.Success(amounts);
    }

    /// <inheritdoc />
    public async Task<Result<StaffBudget>> BuildStaffBudgetAsync(long companyId, long budgetId, string salaryAccountCode, decimal incrementPercent, decimal vacancyPercent, decimal contributionPercent,
        string? contributionAccountCode, CancellationToken ct = default)
    {
        if (vacancyPercent is < 0m or >= 100m || incrementPercent < 0m || contributionPercent < 0m || (contributionPercent > 0m && string.IsNullOrWhiteSpace(contributionAccountCode)))
        {
            return Error.Validation("Budget.StaffInvalid", "Percentages cannot be negative, the vacancy factor is under 100%, and contributions need their own account.");
        }

        Result<Budget> budget = await OpenBudgetAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget.IsFailure)
        {
            return budget.Error;
        }

        string? department = budget.Value.Department;
        List<Core.Payroll.EstablishmentPost> posts = await _db.EstablishmentPosts.AsNoTracking()
            .Where(p => p.CompanyId == companyId && (department == null || p.Department == department)).ToListAsync(ct).ConfigureAwait(false);
        decimal salaries = Math.Round(posts.Sum(p => p.ApprovedPosts * p.AnnualCostPerPost) * (1m + incrementPercent / 100m) * (1m - vacancyPercent / 100m), 2, MidpointRounding.AwayFromZero);
        decimal contributions = Math.Round(salaries * contributionPercent / 100m, 2, MidpointRounding.AwayFromZero);

        Result<IReadOnlyList<decimal>> spread = await SpreadAsync(companyId, budgetId, salaryAccountCode, salaries, SpreadProfile.Even, ct: ct).ConfigureAwait(false);
        if (spread.IsFailure)
        {
            return spread.Error;
        }

        if (contributions > 0m)
        {
            Result<IReadOnlyList<decimal>> spreadContributions = await SpreadAsync(companyId, budgetId, contributionAccountCode!, contributions, SpreadProfile.Even, ct: ct).ConfigureAwait(false);
            if (spreadContributions.IsFailure)
            {
                return spreadContributions.Error;
            }
        }

        return new StaffBudget(posts.Sum(p => p.ApprovedPosts), salaries, contributions);
    }

    /// <inheritdoc />
    public async Task<Result<long>> RequestRephaseAsync(long companyId, long budgetId, string accountCode, int fromPeriod, int toPeriod, decimal amount, string reason, long userId,
        string? costCentre = null, CancellationToken ct = default)
    {
        Budget? budget = await _db.Set<Budget>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        string account = accountCode.Trim().ToUpperInvariant();
        decimal held = budget.Lines.Where(l => l.AccountCode == account && l.Period == fromPeriod && l.CostCentre == costCentre).Sum(l => l.Amount);
        if (!budget.IsSealed || amount <= 0m || fromPeriod == toPeriod || string.IsNullOrWhiteSpace(reason) || amount > held)
        {
            return Error.Rule("Budget.RephaseInvalid", $"Re-phasing moves a positive amount, up to the {held:0.00} the period holds, between two periods of an approved budget, with a reason.");
        }

        BudgetRephase rephase = new(companyId, budgetId, account, costCentre, fromPeriod, toPeriod, amount, reason.Trim(), userId);
        _db.Add(rephase);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rephase.Id;
    }

    /// <inheritdoc />
    public async Task<Result> DecideRephaseAsync(long companyId, long rephaseId, bool approve, long userId, CancellationToken ct = default)
    {
        BudgetRephase? rephase = await _db.Set<BudgetRephase>().FirstOrDefaultAsync(r => r.Id == rephaseId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rephase is null || rephase.Status != RephaseStatus.Pending)
        {
            return Result.Failure(Error.NotFound("Budget.RephaseNotFound", $"No pending re-phasing {rephaseId}."));
        }

        if (rephase.RequestedByUserId == userId)
        {
            return Result.Failure(Error.Rule("Budget.SameUser", "The person who asked for a re-phasing cannot approve it."));
        }

        rephase.DecidedByUserId = userId;
        rephase.Status = approve ? RephaseStatus.Approved : RephaseStatus.Rejected;
        if (approve)
        {
            Budget budget = await _db.Set<Budget>().Include(b => b.Lines).SingleAsync(b => b.Id == rephase.BudgetId, ct).ConfigureAwait(false);
            budget.AddLine(rephase.AccountCode, rephase.FromPeriod, -rephase.Amount, rephase.CostCentre, rephaseId: rephase.Id);
            budget.AddLine(rephase.AccountCode, rephase.ToPeriod, rephase.Amount, rephase.CostCentre, rephaseId: rephase.Id);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // Sets an account's preparation line for a period, adding it when new (virement and re-phasing lines are left alone).
    private static BudgetLine Place(Budget budget, string account, int period, string? costCentre, decimal amount)
    {
        List<BudgetLine> lines = [.. budget.Lines.Where(l => l.AccountCode == account && l.Period == period && l.CostCentre == costCentre && l.VirementId is null && l.RephaseId is null)];
        if (lines.Count == 0)
        {
            return budget.AddLine(account, period, amount, costCentre);
        }

        lines[0].SetAmount(amount);
        lines.Skip(1).ToList().ForEach(l => l.SetAmount(0m));
        return lines[0];
    }

    private async Task<Result<Budget>> OpenBudgetAsync(long companyId, long budgetId, CancellationToken ct)
    {
        Budget? budget = await _db.Set<Budget>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        return budget.IsSealed || budget.Status is not (BudgetStatus.Draft or BudgetStatus.Returned or BudgetStatus.UnderReview)
            ? Error.Rule("Budget.WrongStage", $"Budget {budget.Label} v{budget.VersionNumber} is {budget.Status}; only a budget in preparation can be changed.")
            : budget;
    }

    // The prior fiscal year's actuals on the account, period by period, as weights.
    private async Task<IReadOnlyList<decimal>> PriorYearWeightsAsync(long companyId, FiscalYear year, int periodCount, string account, CancellationToken ct)
    {
        FiscalYear? prior = await _db.FiscalYears.AsNoTracking().Include(y => y.Periods).Where(y => y.CompanyId == companyId && y.EndDate < year.StartDate)
            .OrderByDescending(y => y.EndDate).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (prior is null)
        {
            return [.. Enumerable.Repeat(1m, periodCount)];
        }

        var actuals = await _db.Journals.AsNoTracking()
            .Where(j => j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate >= prior.StartDate && j.PostingDate <= prior.EndDate)
            .SelectMany(j => j.Lines.Where(l => l.AccountCode == account).Select(l => new { j.PostingDate, Amount = l.BaseDebit - l.BaseCredit }))
            .ToListAsync(ct).ConfigureAwait(false);
        List<FiscalPeriod> priorPeriods = [.. prior.OrderedPeriods];
        return [.. Enumerable.Range(0, periodCount).Select(i => i < priorPeriods.Count
            ? Math.Abs(actuals.Where(a => a.PostingDate >= priorPeriods[i].StartDate && a.PostingDate <= priorPeriods[i].EndDate).Sum(a => a.Amount))
            : 0m)];
    }
}
