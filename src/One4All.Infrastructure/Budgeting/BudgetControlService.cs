using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
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
    public BudgetControlService(One4AllDbContext db, IBudgetingService budgeting, Application.Abstractions.Security.IPermissionService? permissions = null)
    {
        _db = db;
        _budgeting = budgeting;
        _permissions = permissions;
    }

    private readonly Application.Abstractions.Security.IPermissionService? _permissions;

    /// <inheritdoc />
    public async Task<Result> SetVirementBandsAsync(long companyId, IReadOnlyList<VirementBandRule> bands, CancellationToken ct = default)
    {
        if (VirementBanding.Problem(bands) is string problem)
        {
            return Result.Failure(Error.Validation("Budget.Virement.Bands", problem));
        }

        _db.Set<VirementBand>().RemoveRange(await _db.Set<VirementBand>().Where(b => b.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false));
        _db.Set<VirementBand>().AddRange(bands.Select(b => new VirementBand(companyId, b.UpToAmount, b.Level)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
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
        string? fromCentre = string.IsNullOrWhiteSpace(request.FromCostCentre) ? null : request.FromCostCentre.Trim().ToUpperInvariant();
        string? toCentre = string.IsNullOrWhiteSpace(request.ToCostCentre) ? null : request.ToCostCentre.Trim().ToUpperInvariant();
        long? toBudget = request.ToBudgetId is long other && other != budgetId ? other : null;
        if (from == to && fromCentre == toCentre && toBudget is null)
        {
            return Error.Validation("Budget.Virement.SameLine", "A virement moves budget between two different lines.");
        }

        Budget? budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null || !budget.IsActive)
        {
            return Error.Rule("Budget.Virement.NotActive", "Virements are made within an active budget.");
        }

        // Between votes of different departments: the receiving budget is another active budget of the same year and type.
        if (toBudget is long target && !await _db.Budgets.AnyAsync(b => b.Id == target && b.CompanyId == companyId && b.IsActive && b.FiscalYearId == budget.FiscalYearId && b.Type == budget.Type, ct)
            .ConfigureAwait(false))
        {
            return Error.Rule("Budget.Virement.TargetBudget", "The receiving budget must be another active budget of the same year and type.");
        }

        if (await SourceShortAsync(budget, from, request.Amount, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        BudgetVirement virement = new(companyId, budgetId, from, to, request.Period, request.Amount, request.Justification.Trim(), requestedByUserId)
        {
            FromCostCentre = fromCentre, ToCostCentre = toCentre, ToBudgetId = toBudget,
        };
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

        // Delegated limits: the band the amount falls in sets the authorisation level the approver needs (one more between budgets).
        List<VirementBandRule> bands = await _db.Set<VirementBand>().AsNoTracking().Where(b => b.CompanyId == companyId).Select(b => new VirementBandRule(b.UpToAmount, b.Level))
            .ToListAsync(ct).ConfigureAwait(false);
        if (VirementBanding.RequiredLevel(bands, virement.Amount, virement.ToBudgetId is not null) is int level && _permissions is not null)
        {
            Core.Security.EffectivePermissions approver = await _permissions.ResolveAsync(approverUserId, ct).ConfigureAwait(false);
            if (!Core.Security.OperationFlagsExtensions.AuthoriseLevels.Skip(level - 1).Any(flag => approver.Has("Budget.VirementApprove", flag)))
            {
                return Result.Failure(Error.Forbidden("Budget.Virement.Band", $"A virement of {virement.Amount:0.00} needs authorisation level {level} on Virement Approval."));
            }
        }

        if (!virement.Approve(approverUserId))
        {
            return Result.Failure(Error.Rule("Budget.Virement.SelfApproval", "A different user must approve a virement than the one who requested it."));
        }

        Budget receiving = virement.ToBudgetId is long target ? await _db.Budgets.Include(b => b.Lines).FirstAsync(b => b.Id == target, ct).ConfigureAwait(false) : budget;
        budget.AddLine(virement.FromAccountCode, virement.Period, -virement.Amount, virement.FromCostCentre, virement.Id);
        receiving.AddLine(virement.ToAccountCode, virement.Period, virement.Amount, virement.ToCostCentre, virement.Id);
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
    public async Task<IReadOnlyList<VoteBookRow>> VoteBookAsync(long companyId, long fiscalYearId, int asAtPeriod, CancellationToken ct = default, bool byFund = false)
    {
        if (byFund)
        {
            return await VoteBookByFundAsync(companyId, fiscalYearId, asAtPeriod, ct).ConfigureAwait(false);
        }

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

    // The vote book kept by fund (brief §3.11): each vote is an account in a fund, so expenditure comes from the posted journal lines (period balances carry no fund).
    // Spend or commitments in a fund the account has no appropriation in fall to the account's row with no fund.
    private async Task<IReadOnlyList<VoteBookRow>> VoteBookByFundAsync(long companyId, long fiscalYearId, int asAtPeriod, CancellationToken ct)
    {
        var lines = await _db.Budgets.AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.IsActive
                        && (b.Type == BudgetType.OriginalApproved || b.Type == BudgetType.SupplementaryRevised))
            .SelectMany(b => b.Lines.Select(l => new { b.Type, l.AccountCode, l.Fund, l.Amount, IsVirement = l.VirementId != null }))
            .ToListAsync(ct).ConfigureAwait(false);
        var spent = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            join period in _db.Set<FiscalPeriod>() on j.FiscalPeriodId equals period.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && period.FiscalYearId == fiscalYearId && period.PeriodNumber <= asAtPeriod
            group l by new { l.AccountCode, l.Fund } into g
            select new { g.Key.AccountCode, g.Key.Fund, Movement = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);
        var committed = await _db.Set<BudgetCommitment>()
            .Where(c => c.CompanyId == companyId && c.FiscalYearId == fiscalYearId && c.Status == CommitmentStatus.Open && c.Period <= asAtPeriod)
            .GroupBy(c => new { c.AccountCode, c.Fund }).Select(g => new { g.Key.AccountCode, g.Key.Fund, Amount = g.Sum(c => c.Amount) })
            .ToListAsync(ct).ConfigureAwait(false);

        List<(string Account, string? Fund)> votes = [.. lines.Select(l => (l.AccountCode, l.Fund)).Distinct()
            .OrderBy(v => v.AccountCode, StringComparer.Ordinal).ThenBy(v => v.Fund ?? string.Empty, StringComparer.Ordinal)];
        HashSet<(string, string?)> keyed = [.. votes];
        bool Falls(string account, string? fund, string? rowFund) => fund == rowFund || (rowFund is null && !keyed.Contains((account, fund)));
        List<string> codes = [.. votes.Select(v => v.Account).Distinct()];
        Dictionary<string, string> names = await _db.Accounts.AsNoTracking()
            .Where(a => a.CompanyId == companyId && codes.Contains(a.AccountCode))
            .ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);
        return [.. votes.Select(v => new VoteBookRow(
            v.Account,
            names.GetValueOrDefault(v.Account, v.Account),
            lines.Where(l => l.AccountCode == v.Account && l.Fund == v.Fund && !l.IsVirement && l.Type == BudgetType.OriginalApproved).Sum(l => l.Amount),
            lines.Where(l => l.AccountCode == v.Account && l.Fund == v.Fund && !l.IsVirement && l.Type == BudgetType.SupplementaryRevised).Sum(l => l.Amount),
            lines.Where(l => l.AccountCode == v.Account && l.Fund == v.Fund && l.IsVirement).Sum(l => l.Amount),
            spent.Where(s => s.AccountCode == v.Account && Falls(v.Account, s.Fund, v.Fund)).Sum(s => s.Movement),
            committed.Where(c => c.AccountCode == v.Account && Falls(v.Account, c.Fund, v.Fund)).Sum(c => c.Amount),
            v.Fund))];
    }

    /// <inheritdoc />
    public Task<Result<CommitmentYearEnd>> CloseYearCommitmentsAsync(long companyId, long fiscalYearId, long nextFiscalYearId, IReadOnlyCollection<long> carryForwardIds, long approvedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => CloseYearCommitmentsCoreAsync(companyId, fiscalYearId, nextFiscalYearId, carryForwardIds, approvedByUserId, ct), ct);

    // One unit: every commitment of the year is decided, or none is.
    private async Task<Result<CommitmentYearEnd>> CloseYearCommitmentsCoreAsync(long companyId, long fiscalYearId, long nextFiscalYearId, IReadOnlyCollection<long> carryForwardIds, long approvedByUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(carryForwardIds);
        List<FiscalYear> years = await _db.FiscalYears.AsNoTracking()
            .Where(y => y.CompanyId == companyId && (y.Id == fiscalYearId || y.Id == nextFiscalYearId)).ToListAsync(ct).ConfigureAwait(false);
        FiscalYear? ending = years.FirstOrDefault(y => y.Id == fiscalYearId);
        FiscalYear? next = years.FirstOrDefault(y => y.Id == nextFiscalYearId);
        if (ending is null || next is null || next.StartDate <= ending.EndDate)
        {
            return Error.Rule("Budget.YearEnd.Years", "Commitments carry forward from a year into a later one of the same company.");
        }

        List<BudgetCommitment> open = await _db.Set<BudgetCommitment>()
            .Where(c => c.CompanyId == companyId && c.FiscalYearId == fiscalYearId && c.Status == CommitmentStatus.Open)
            .OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        long? stray = carryForwardIds.Cast<long?>().FirstOrDefault(id => open.All(c => c.Id != id));
        if (stray is not null)
        {
            return Error.Rule("Budget.YearEnd.NotOpen", $"Commitment {stray} is not open in the year ending.");
        }

        List<(BudgetCommitment Original, BudgetCommitment? Carried)> decided = [];
        foreach (BudgetCommitment commitment in open)
        {
            if (carryForwardIds.Contains(commitment.Id))
            {
                BudgetCommitment carried = commitment.CarryForward(nextFiscalYearId, approvedByUserId)!;
                _db.Set<BudgetCommitment>().Add(carried);
                decided.Add((commitment, carried));
            }
            else
            {
                commitment.Lapse(approvedByUserId);
                decided.Add((commitment, null));
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        YearEndCommitment Row((BudgetCommitment Original, BudgetCommitment? Carried) d) => new(d.Original.Id, d.Original.AccountCode, d.Original.Reference, d.Original.Amount, d.Carried?.Id);
        return new CommitmentYearEnd([.. decided.Where(d => d.Carried is null).Select(Row)], [.. decided.Where(d => d.Carried is not null).Select(Row)]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UtilisationAlert>> UtilisationAlertsAsync(long companyId, long fiscalYearId, int asAtPeriod, IReadOnlyList<decimal> thresholdPercents, CancellationToken ct = default)
    {
        IReadOnlyList<VoteBookRow> votes = await VoteBookAsync(companyId, fiscalYearId, asAtPeriod, ct).ConfigureAwait(false);
        List<UtilisationAlert> alerts = [];
        foreach (VoteBookRow vote in votes)
        {
            decimal used = vote.Expenditure + vote.Commitments;
            decimal percent = BudgetUtilisation.PercentUsed(vote.Appropriation, used);
            if (BudgetUtilisation.HighestCrossed(percent, thresholdPercents) is decimal crossed)
            {
                alerts.Add(new UtilisationAlert(vote.AccountCode, vote.Name, vote.Appropriation, used, percent, crossed));
            }
        }

        return [.. alerts.OrderByDescending(a => a.PercentUsed).ThenBy(a => a.AccountCode, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<Result<bool>> VerifySealAsync(long companyId, long budgetId, CancellationToken ct = default)
    {
        Budget? budget = await _db.Budgets.AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        if (!budget.IsSealed)
        {
            return Error.Rule("Budget.NotSealed", $"Budget {budgetId} has not been approved, so it has no seal.");
        }

        return budget.VerifySeal();
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
