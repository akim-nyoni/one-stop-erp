using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>A material variance with its explanation, if one has been given.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Budget">The budget to the as-at period.</param>
/// <param name="Actual">The actual to the as-at period.</param>
/// <param name="Comment">The budget holder's explanation, or null while still owed.</param>
/// <param name="CommentedByUserId">Who explained it.</param>
public sealed record MaterialVarianceRow(string AccountCode, decimal Budget, decimal Actual, string? Comment, long? CommentedByUserId)
{
    /// <summary>The variance (budget − actual).</summary>
    public decimal Variance => Budget - Actual;

    /// <summary>Whether an explanation is still owed.</summary>
    public bool AwaitingComment => Comment is null;
}

/// <summary>
/// Variance commentary (brief §4.18.6): lists the variances against the active budget that are material enough to need an
/// explanation, with any explanation given, and records budget holders' explanations.
/// </summary>
public interface IVarianceCommentaryService
{
    /// <summary>Lists the material variances to an as-at period, with their explanations.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="type">The budget compared against.</param>
    /// <param name="department">The department, or null for the company-wide budget.</param>
    /// <param name="asAtPeriod">The last period included.</param>
    /// <param name="threshold">What counts as material.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The material variances, by account, or why there are none to show.</returns>
    Task<Result<IReadOnlyList<MaterialVarianceRow>>> MaterialVariancesAsync(long companyId, long fiscalYearId, BudgetType type, string? department, int asAtPeriod, VarianceThreshold threshold, CancellationToken ct = default);

    /// <summary>Records (or replaces) the explanation of an account's variance to a period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="asAtPeriod">The period.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="comment">The explanation; required.</param>
    /// <param name="authorUserId">Who explains it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CommentAsync(long companyId, long fiscalYearId, int asAtPeriod, string accountCode, string comment, long authorUserId, CancellationToken ct = default);
}
