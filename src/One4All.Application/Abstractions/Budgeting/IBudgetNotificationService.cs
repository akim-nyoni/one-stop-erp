using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>A department's place on the preparation board.</summary>
/// <param name="Department">The department.</param>
/// <param name="BudgetHolder">Its budget holder.</param>
/// <param name="Deadline">When its submission is due (null: none set).</param>
/// <param name="Status">Its latest budget's status for the year (null: not started).</param>
/// <param name="DaysLeft">Days to the deadline (negative: overdue; null: no deadline).</param>
public sealed record PreparationStatus(string Department, string? BudgetHolder, DateOnly? Deadline, string? Status, int? DaysLeft);

/// <summary>What a notification run sent.</summary>
/// <param name="Messages">Messages queued.</param>
/// <param name="Items">Alerts or departments covered.</param>
public sealed record BudgetNotices(int Messages, int Items);

/// <summary>
/// Budget-season notices (brief §4.18: "deadline management… reminders" and "notifications at configurable utilisation thresholds"): per-department
/// submission deadlines on a status board with reminders to budget holders, and utilisation alerts emailed once as each threshold is crossed.
/// </summary>
public interface IBudgetNotificationService
{
    /// <summary>Sets (or clears) a department's submission deadline.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department.</param>
    /// <param name="deadline">The deadline (null: none).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetDeadlineAsync(long companyId, string department, DateOnly? deadline, CancellationToken ct = default);

    /// <summary>The preparation board for a year: each department's deadline and where its budget stands.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year being budgeted.</param>
    /// <param name="asAt">Today.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The board.</returns>
    Task<IReadOnlyList<PreparationStatus>> BoardAsync(long companyId, long fiscalYearId, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Emails each budget holder whose department has not submitted and whose deadline is within the days given (or past).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year being budgeted.</param>
    /// <param name="asAt">Today.</param>
    /// <param name="daysBefore">How far ahead to remind.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was sent.</returns>
    Task<Result<BudgetNotices>> RemindAsync(long companyId, long fiscalYearId, DateOnly asAt, int daysBefore, CancellationToken ct = default);

    /// <summary>
    /// Emails the company's budget alert address the votes that have crossed a utilisation threshold (the company's, e.g. 75 / 90 / 100%) since the
    /// last alert — each vote and threshold once.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The date (its fiscal year and period are used).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was sent.</returns>
    Task<Result<BudgetNotices>> AlertAsync(long companyId, DateOnly asAt, CancellationToken ct = default);
}
