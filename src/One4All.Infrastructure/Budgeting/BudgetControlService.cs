using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Virements, the virement register and the vote book over the budget schema and GL actuals.</summary>
public sealed class BudgetControlService : IBudgetControlService
{
    private const int LastPeriod = 12;

    private readonly One4AllDbContext _db;
    private readonly IBudgetingService _budgeting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="budgeting">The budgeting service, for availability.</param>
    public BudgetControlService(One4AllDbContext db, IBudgetingService budgeting)
    {
        _db = db;
        _budgeting = budgeting;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RequestVirementAsync(long companyId, long budgetId, VirementRequest request, long requestedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m || request.Period is < 1 or > LastPeriod || string.IsNullOrWhiteSpace(request.Justification))
        {
            return Error.Validation("Budget.Virement.Invalid", "A virement needs a positive amount, a period from 1 to 12 and a justification.");
        }

        string from = request.FromAccountCode.Trim();
        string to = request.ToAccountCode.Trim();
        if (from == to)
        {
            return Error.Validation("Budget.Virement.SameLine", "A virement moves budget between two different lines.");
        }

        Budget? budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null || !budget.IsActive)
        {
            return Error.Rule("Budget.Virement.NotActive", "Virements are made within an active budget.");
        }

        if (await SourceShortAsync(budget, from, request.Amount, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        BudgetVirement virement = new(companyId, budgetId, from, to, request.Period, request.Amount, request.Justification.Trim(), requestedByUserId);
        _db.Set<BudgetVirement>().Add(virement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return virement.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ApproveVirementAsync(long companyId, long virementId, long approverUserId, CancellationToken ct = default)
    {
        BudgetVirement? virement = await _db.Set<BudgetVirement>().FirstOrDefaultAsync(v => v.Id == virementId && v.CompanyId == companyId, ct).ConfigureAwait(false);
        if (virement is null || virement.Status != VirementStatus.Pending)
        {
            return Result.Failure(Error.Rule("Budget.Virement.NotPending", $"Virement {virementId} is not awaiting approval."));
        }

        Budget budget = await _db.Budgets.Include(b => b.Lines).FirstAsync(b => b.Id == virement.BudgetId, ct).ConfigureAwait(false);
        if (!budget.IsActive)
        {
            return Result.Failure(Error.Rule("Budget.Virement.NotActive", "The budget is no longer active."));
        }

        // The source may have been spent or moved since the request: check again.
        if (await SourceShortAsync(budget, virement.FromAccountCode, virement.Amount, ct).ConfigureAwait(false) is { } refused)
        {
            return Result.Failure(refused);
        }

        if (!virement.Approve(approverUserId))
        {
            return Result.Failure(Error.Rule("Budget.Virement.SelfApproval", "A different user must approve a virement than the one who requested it."));
        }

        budget.AddLine(virement.FromAccountCode, virement.Period, -virement.Amount, null, virement.Id);
        budget.AddLine(virement.ToAccountCode, virement.Period, virement.Amount, null, virement.Id);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RejectVirementAsync(long companyId, long virementId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Budget.Virement.ReasonRequired", "Rejecting a virement requires a reason."));
        }

        BudgetVirement? virement = await _db.Set<BudgetVirement>().FirstOrDefaultAsync(v => v.Id == virementId && v.CompanyId == companyId, ct).ConfigureAwait(false);
        if (virement is null || !virement.Reject(userId, reason.Trim()))
        {
            return Result.Failure(Error.Rule("Budget.Virement.NotPending", $"Virement {virementId} is not awaiting approval."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<VirementRegisterRow>>> VirementRegisterAsync(long companyId, long budgetId, CancellationToken ct = default)
    {
        Budget? budget = await _db.Budgets.AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        List<VirementRegisterRow> rows = [.. budget.Lines
            .GroupBy(l => l.AccountCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new VirementRegisterRow(
                g.Key,
                g.Where(l => l.VirementId is null).Sum(l => l.Amount),
                g.Where(l => l.VirementId is not null && l.Amount > 0m).Sum(l => l.Amount),
                -g.Where(l => l.VirementId is not null && l.Amount < 0m).Sum(l => l.Amount)))];
        return Result<IReadOnlyList<VirementRegisterRow>>.Success(rows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VoteBookRow>> VoteBookAsync(long companyId, long fiscalYearId, int asAtPeriod, CancellationToken ct = default)
    {
        var lines = await _db.Budgets.AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.IsActive
                        && (b.Type == BudgetType.OriginalApproved || b.Type == BudgetType.SupplementaryRevised))
            .SelectMany(b => b.Lines.Select(l => new { b.Type, l.AccountCode, l.Amount, IsVirement = l.VirementId != null }))
            .ToListAsync(ct).ConfigureAwait(false);

        // Expenditure from the GL period balances to the as-at period: budgets never post, so actuals come only from here.
        Dictionary<string, decimal> expenditure = await (
            from bal in _db.Set<GlPeriodBalance>()
            join period in _db.Set<FiscalPeriod>() on bal.FiscalPeriodId equals period.Id
            where bal.CompanyId == companyId && bal.FiscalYearId == fiscalYearId && period.PeriodNumber <= asAtPeriod
            group bal by bal.AccountCode into g
            select new { AccountCode = g.Key, Movement = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) })
            .ToDictionaryAsync(x => x.AccountCode, x => x.Movement, ct).ConfigureAwait(false);

        Dictionary<string, decimal> commitments = await _db.Set<BudgetCommitment>()
            .Where(c => c.CompanyId == companyId && c.FiscalYearId == fiscalYearId && c.Status == CommitmentStatus.Open && c.Period <= asAtPeriod)
            .GroupBy(c => c.AccountCode)
            .Select(g => new { AccountCode = g.Key, Amount = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(x => x.AccountCode, x => x.Amount, ct).ConfigureAwait(false);

        List<string> votes = [.. lines.Select(l => l.AccountCode).Distinct().Order(StringComparer.Ordinal)];
        Dictionary<string, string> names = await _db.Accounts.AsNoTracking()
            .Where(a => a.CompanyId == companyId && votes.Contains(a.AccountCode))
            .ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);

        return [.. votes.Select(code => new VoteBookRow(
            code,
            names.GetValueOrDefault(code, code),
            lines.Where(l => l.AccountCode == code && !l.IsVirement && l.Type == BudgetType.OriginalApproved).Sum(l => l.Amount),
            lines.Where(l => l.AccountCode == code && !l.IsVirement && l.Type == BudgetType.SupplementaryRevised).Sum(l => l.Amount),
            lines.Where(l => l.AccountCode == code && l.IsVirement).Sum(l => l.Amount),
            expenditure.GetValueOrDefault(code),
            commitments.GetValueOrDefault(code)))];
    }

    // Whether the source line lacks the budget to give: its year's budget less expenditure and open commitments.
    private async Task<Error?> SourceShortAsync(Budget budget, string accountCode, decimal amount, CancellationToken ct)
    {
        Result<AvailabilityResult> available = await _budgeting.CheckAvailabilityAsync(budget.CompanyId, budget.FiscalYearId, budget.Type, budget.Department, accountCode, amount, LastPeriod, ct).ConfigureAwait(false);
        if (available.IsFailure)
        {
            return available.Error;
        }

        return available.Value.Passes
            ? null
            : Error.Rule("Budget.Virement.ExceedsAvailable", $"Line {accountCode} has {available.Value.Available:0.00} available, less than the {amount:0.00} to move.");
    }
}
