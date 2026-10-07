using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>An indicative outer-year amount.</summary>
/// <param name="YearOffset">Years after the budget year (1–4).</param>
/// <param name="AccountCode">The account.</param>
/// <param name="Amount">The annual amount.</param>
/// <param name="CostCentre">The cost centre, if any.</param>
public sealed record OuterYearAmount(int YearOffset, string AccountCode, decimal Amount, string? CostCentre = null);

/// <summary>One account's row in the multi-year framework.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="CostCentre">The cost centre.</param>
/// <param name="Years">Year 1 (the appropriated budget) then the indicative years.</param>
public sealed record FrameworkRow(string AccountCode, string? CostCentre, IReadOnlyList<decimal> Years);

/// <summary>
/// The multi-year (MTEF-style) framework and the end of a budget's life (brief §4.18): year 1 is the appropriated budget and up to four outer years
/// are indicative; rolling forward makes next year's draft from them; superseded versions are archived (seal verified) and then locked for good.
/// </summary>
public interface IBudgetFrameworkService
{
    /// <summary>Sets a budget's indicative outer years (before it is approved).</summary>
    Task<Result> SetOuterYearsAsync(long companyId, long budgetId, IReadOnlyList<OuterYearAmount> amounts, CancellationToken ct = default);

    /// <summary>The framework: year 1 from the budget's lines and the outer years, by account.</summary>
    Task<Result<IReadOnlyList<FrameworkRow>>> FrameworkAsync(long companyId, long budgetId, CancellationToken ct = default);

    /// <summary>
    /// Rolls an approved framework forward into a new draft for the next fiscal year: its first outer year becomes the year spread evenly over
    /// twelve periods, the later outer years move up one, and the last carries on as the new final year.
    /// </summary>
    Task<Result<long>> RollForwardAsync(long companyId, long budgetId, long nextFiscalYearId, string label, CancellationToken ct = default);

    /// <summary>Archives a superseded budget (its seal verified).</summary>
    Task<Result> ArchiveAsync(long companyId, long budgetId, CancellationToken ct = default);

    /// <summary>Locks an archived budget for good.</summary>
    Task<Result> LockAsync(long companyId, long budgetId, CancellationToken ct = default);
}
