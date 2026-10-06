using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Licensing.Core;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Budget preparation through review, negotiation rounds and approval (brief §4.18.4).</summary>
public sealed class BudgetPreparationService : IBudgetPreparationService
{
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">Stamps each step.</param>
    public BudgetPreparationService(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result> SubmitAsync(long companyId, long budgetId, long userId, string? comment = null, CancellationToken ct = default)
        => StepAsync(companyId, budgetId, [BudgetStatus.Draft, BudgetStatus.Returned], b => b.Submit(userId), BudgetReviewAction.Submitted, userId, comment, ct);

    /// <inheritdoc />
    public Task<Result> StartReviewAsync(long companyId, long budgetId, long userId, CancellationToken ct = default)
        => StepAsync(companyId, budgetId, [BudgetStatus.Submitted], b => b.MoveTo(BudgetStatus.UnderReview), BudgetReviewAction.ReviewStarted, userId, null, ct);

    /// <inheritdoc />
    public async Task<Result> ProposeAsync(long companyId, long budgetId, long userId, IReadOnlyList<ProposedFigure> figures, CancellationToken ct = default)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return NotFound(budgetId);
        }

        if (budget.Status != BudgetStatus.UnderReview || figures.Count == 0 || figures.Any(f => f.Amount < 0m))
        {
            return Result.Failure(Error.Rule("Budget.WrongStage", "Figures are proposed while a budget is under review, and are not negative."));
        }

        foreach (ProposedFigure figure in figures)
        {
            string account = figure.AccountCode.Trim().ToUpperInvariant();
            decimal requested = budget.Lines.Where(l => l.AccountCode == account && l.Period == figure.Period && l.CostCentre == figure.CostCentre && l.VirementId is null).Sum(l => l.Amount);
            _db.Add(new BudgetProposal(budget.Id, budget.Round, account, figure.CostCentre, figure.Period, requested, figure.Amount, figure.Comment));
        }

        _db.Add(new BudgetReviewEvent(budget.Id, budget.Round, BudgetReviewAction.Proposed, userId, _clock.UtcNow, $"{figures.Count} figure(s) proposed"));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> ReturnAsync(long companyId, long budgetId, long userId, string comment, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(comment)
            ? Task.FromResult(Result.Failure(Error.Validation("Budget.ReasonRequired", "Say why the budget is returned.")))
            : StepAsync(companyId, budgetId, [BudgetStatus.UnderReview], b => b.MoveTo(BudgetStatus.Returned), BudgetReviewAction.Returned, userId, comment, ct);

    /// <inheritdoc />
    public async Task<Result<int>> AcceptProposalsAsync(long companyId, long budgetId, long userId, CancellationToken ct = default)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        if (budget.Status != BudgetStatus.Returned)
        {
            return Error.Rule("Budget.WrongStage", "Proposals are accepted on a returned budget.");
        }

        List<BudgetProposal> proposals = await _db.Set<BudgetProposal>().Where(p => p.BudgetId == budgetId && p.Round == budget.Round).OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        int changed = 0;
        foreach (BudgetProposal proposal in proposals)
        {
            changed += Set(budget, proposal.AccountCode, proposal.Period, proposal.CostCentre, proposal.Proposed) ? 1 : 0;
        }

        _db.Add(new BudgetReviewEvent(budget.Id, budget.Round, BudgetReviewAction.ProposalsAccepted, userId, _clock.UtcNow, $"{changed} line(s) changed"));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return changed;
    }

    /// <inheritdoc />
    public async Task<Result> ReviseAsync(long companyId, long budgetId, long userId, ProposedFigure figure, CancellationToken ct = default)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return NotFound(budgetId);
        }

        if (budget.Status is not (BudgetStatus.Draft or BudgetStatus.Returned) || budget.IsSealed || figure.Amount < 0m)
        {
            return Result.Failure(Error.Rule("Budget.WrongStage", "Figures are revised on a draft or returned budget, and are not negative."));
        }

        string account = figure.AccountCode.Trim().ToUpperInvariant();
        Set(budget, account, figure.Period, figure.CostCentre, figure.Amount);
        _db.Add(new BudgetReviewEvent(budget.Id, budget.Round, BudgetReviewAction.Revised, userId, _clock.UtcNow, $"{account} period {figure.Period}: {figure.Amount:0.00}{(figure.Comment is null ? string.Empty : $" ({figure.Comment})")}"));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> RecommendAsync(long companyId, long budgetId, long userId, string? comment = null, CancellationToken ct = default)
        => StepAsync(companyId, budgetId, [BudgetStatus.UnderReview], b => b.MoveTo(BudgetStatus.Recommended), BudgetReviewAction.Recommended, userId, comment, ct);

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, long budgetId, long userId, string resolutionReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resolutionReference))
        {
            return Result.Failure(Error.Validation("Budget.ResolutionRequired", "Record the board or council resolution approving the budget."));
        }

        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return NotFound(budgetId);
        }

        // Segregation of duties: whoever prepared and submitted it does not approve it.
        if (budget.SubmittedByUserId == userId)
        {
            return Result.Failure(Error.Rule("Budget.SameUser", "The person who submitted a budget cannot approve it."));
        }

        return await StepAsync(companyId, budgetId, [BudgetStatus.Recommended], b => b.Approve(userId, resolutionReference.Trim()), BudgetReviewAction.Approved, userId, resolutionReference.Trim(), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<BudgetTrail>> TrailAsync(long companyId, long budgetId, CancellationToken ct = default)
    {
        Budget? budget = await _db.Set<Budget>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found.");
        }

        List<BudgetReviewEvent> events = await _db.Set<BudgetReviewEvent>().AsNoTracking().Where(e => e.BudgetId == budgetId).OrderBy(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
        List<BudgetProposal> proposals = await _db.Set<BudgetProposal>().AsNoTracking().Where(p => p.BudgetId == budgetId).OrderBy(p => p.Round).ThenBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        return new BudgetTrail(budget.Status, budget.Round, budget.ResolutionReference, events, proposals);
    }

    private static Result NotFound(long budgetId) => Result.Failure(Error.NotFound("Budget.NotFound", $"Budget {budgetId} not found."));

    // Sets a preparation line's amount, adding the line when it is new; returns whether anything changed.
    private static bool Set(Budget budget, string account, int period, string? costCentre, decimal amount)
    {
        List<BudgetLine> lines = [.. budget.Lines.Where(l => l.AccountCode == account && l.Period == period && l.CostCentre == costCentre && l.VirementId is null)];
        if (lines.Count == 0)
        {
            budget.AddLine(account, period, amount, costCentre);
            return true;
        }

        bool changed = lines[0].Amount != amount || lines.Count > 1;
        lines[0].SetAmount(amount);
        lines.Skip(1).ToList().ForEach(l => l.SetAmount(0m));
        return changed;
    }

    private Task<Budget?> LoadAsync(long companyId, long budgetId, CancellationToken ct)
        => _db.Set<Budget>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId, ct);

    private async Task<Result> StepAsync(long companyId, long budgetId, BudgetStatus[] from, Action<Budget> apply, BudgetReviewAction action, long userId, string? comment, CancellationToken ct)
    {
        Budget? budget = await LoadAsync(companyId, budgetId, ct).ConfigureAwait(false);
        if (budget is null)
        {
            return NotFound(budgetId);
        }

        if (!from.Contains(budget.Status))
        {
            return Result.Failure(Error.Rule("Budget.WrongStage", $"Budget {budget.Label} v{budget.VersionNumber} is {budget.Status}; this step needs it {string.Join(" or ", from)}."));
        }

        apply(budget);
        _db.Add(new BudgetReviewEvent(budget.Id, budget.Round, action, userId, _clock.UtcNow, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
