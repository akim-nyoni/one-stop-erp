using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>One figure an imported workbook would change.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="CostCentre">The cost centre, if any.</param>
/// <param name="Period">The period.</param>
/// <param name="Before">The figure now.</param>
/// <param name="After">The figure in the workbook.</param>
public sealed record BudgetWorkbookChange(string AccountCode, string? CostCentre, int Period, decimal Before, decimal After);

/// <summary>A problem on a row of an imported workbook.</summary>
/// <param name="Row">The Excel row number.</param>
/// <param name="Message">What is wrong.</param>
public sealed record BudgetWorkbookProblem(int Row, string Message);

/// <summary>What an import found and (when committed) did.</summary>
/// <param name="Changes">The figures that differ from the budget.</param>
/// <param name="Problems">Rows that cannot be taken; nothing is applied while there are any.</param>
/// <param name="AnnualTotalBefore">The budget's total now.</param>
/// <param name="AnnualTotalAfter">The total with the changes.</param>
/// <param name="Applied">True when the changes were written.</param>
public sealed record BudgetWorkbookImport(IReadOnlyList<BudgetWorkbookChange> Changes, IReadOnlyList<BudgetWorkbookProblem> Problems, decimal AnnualTotalBefore,
    decimal AnnualTotalAfter, bool Applied);

/// <summary>
/// The Excel round trip for budget preparation (brief §4.18): a budget out to a workbook (one row per account and cost centre, a column
/// per period and a total formula), edited offline, and back in — every row checked (the workbook belongs to this budget, accounts and
/// cost centres exist, figures are numbers and not negative, no row twice), the differences previewed, and applied together only when
/// nothing is wrong and the budget is still a draft or returned. Rows taken out of the workbook leave those figures as they were.
/// </summary>
public interface IBudgetWorkbookService
{
    /// <summary>Exports a budget as an .xlsx workbook.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="includeBudgetAccounts">Also give a zero row for every active budget-enabled account the budget has no line on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workbook bytes and a file name.</returns>
    Task<Result<(byte[] Workbook, string FileName)>> ExportAsync(long companyId, long budgetId, bool includeBudgetAccounts, CancellationToken ct = default);

    /// <summary>Checks an edited workbook against the budget and, when <paramref name="commit"/> and nothing is wrong, applies it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="workbook">The .xlsx bytes.</param>
    /// <param name="userId">Who imports it (recorded in the budget's trail).</param>
    /// <param name="commit">False to preview only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The changes and problems, or why the workbook cannot be read at all.</returns>
    Task<Result<BudgetWorkbookImport>> ImportAsync(long companyId, long budgetId, byte[] workbook, long userId, bool commit, CancellationToken ct = default);
}
