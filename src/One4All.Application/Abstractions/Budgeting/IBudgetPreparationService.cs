using One4All.Core.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>A figure Finance proposes.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Period">The period.</param>
/// <param name="Amount">The proposed amount.</param>
/// <param name="CostCentre">The cost centre, if the line has one.</param>
/// <param name="Comment">Why.</param>
public sealed record ProposedFigure(string AccountCode, int Period, decimal Amount, string? CostCentre = null, string? Comment = null);

/// <summary>A budget's preparation trail.</summary>
/// <param name="Status">Where it stands.</param>
/// <param name="Round">The current negotiation round.</param>
/// <param name="ResolutionReference">The approving resolution, once approved.</param>
/// <param name="Events">Every step, in order.</param>
/// <param name="Proposals">Every proposal, requested beside proposed, by round.</param>
public sealed record BudgetTrail(BudgetStatus Status, int Round, string? ResolutionReference, IReadOnlyList<BudgetReviewEvent> Events, IReadOnlyList<BudgetProposal> Proposals);

/// <summary>
/// Budget preparation through review and approval (brief §4.18.4): the department submits; Finance reviews, proposes figures and returns
/// or recommends; the department accepts proposals or revises and resubmits (each submission a new round, requested and proposed figures
/// both kept); the board or council approves with its resolution reference — never the person who submitted it — and the budget is sealed.
/// </summary>
public interface IBudgetPreparationService
{
    /// <summary>Submits a draft or returned budget, opening a new round.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">Who submits.</param>
    /// <param name="comment">Notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SubmitAsync(long companyId, long budgetId, long userId, string? comment = null, CancellationToken ct = default);

    /// <summary>The departments whose budgets a user may see and prepare, or null when every department (Finance, or a user tied to no department).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The departments, or null for all.</returns>
    Task<IReadOnlySet<string>?> DepartmentsForAsync(long companyId, long userId, CancellationToken ct = default);

    /// <summary>Finance takes up a submitted budget for review.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">The reviewer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> StartReviewAsync(long companyId, long budgetId, long userId, CancellationToken ct = default);

    /// <summary>Finance proposes figures against the department's, kept beside them for the round.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">The reviewer.</param>
    /// <param name="figures">The proposals.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ProposeAsync(long companyId, long budgetId, long userId, IReadOnlyList<ProposedFigure> figures, CancellationToken ct = default);

    /// <summary>Returns a budget under review to the department.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">The reviewer.</param>
    /// <param name="comment">Why (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ReturnAsync(long companyId, long budgetId, long userId, string comment, CancellationToken ct = default);

    /// <summary>The department accepts the round's proposals: the lines take Finance's figures.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">Who accepts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many lines changed.</returns>
    Task<Result<int>> AcceptProposalsAsync(long companyId, long budgetId, long userId, CancellationToken ct = default);

    /// <summary>The department revises a figure on a draft or returned budget (adding the line when new).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">Who revises.</param>
    /// <param name="figure">The new figure.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ReviseAsync(long companyId, long budgetId, long userId, ProposedFigure figure, CancellationToken ct = default);

    /// <summary>The committee or executive recommends a reviewed budget for approval.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">Who recommends.</param>
    /// <param name="comment">Notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RecommendAsync(long companyId, long budgetId, long userId, string? comment = null, CancellationToken ct = default);

    /// <summary>The board or council approves a recommended budget, recording its resolution; the lines are sealed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="userId">Who records the approval (not the submitter).</param>
    /// <param name="resolutionReference">The minute or resolution (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ApproveAsync(long companyId, long budgetId, long userId, string resolutionReference, CancellationToken ct = default);

    /// <summary>The preparation trail.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The trail.</returns>
    Task<Result<BudgetTrail>> TrailAsync(long companyId, long budgetId, CancellationToken ct = default);
}
