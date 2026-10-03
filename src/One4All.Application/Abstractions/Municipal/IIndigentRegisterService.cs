using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A household's means test.</summary>
/// <param name="PropertyId">The property.</param>
/// <param name="HouseholdIncome">Monthly household income.</param>
/// <param name="SubsidyPercent">The share of bills to subsidise.</param>
/// <param name="ApprovedFrom">The first day subsidised.</param>
/// <param name="ReviewDue">When the means test must be reviewed.</param>
/// <param name="ApprovedByUserId">Who approves it.</param>
public sealed record IndigentApplication(long PropertyId, decimal HouseholdIncome, decimal SubsidyPercent, DateOnly ApprovedFrom, DateOnly ReviewDue, long ApprovedByUserId);

/// <summary>One property's subsidy in a run.</summary>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">Its stand number.</param>
/// <param name="Billed">What was billed in the month.</param>
/// <param name="Subsidy">The subsidy credited.</param>
public sealed record SubsidyLine(long PropertyId, string StandNumber, decimal Billed, decimal Subsidy);

/// <summary>A registration whose means test is due for review.</summary>
/// <param name="RegistrationId">The registration.</param>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">Its stand number.</param>
/// <param name="ReviewDue">When the review is due.</param>
/// <param name="Overdue">Whether the date has passed (and subsidy stopped).</param>
public sealed record IndigentReviewDue(long RegistrationId, long PropertyId, string StandNumber, DateOnly ReviewDue, bool Overdue);

/// <summary>
/// The indigent / social-tariff register (brief §4.9): means-tested registrations with review dates, a monthly subsidy
/// run crediting each registered household's bills by its subsidy share (Dr indigent subsidy, Cr the ratepayer's
/// account) once per property per month, and the list of means tests due for review.
/// </summary>
public interface IIndigentRegisterService
{
    /// <summary>Registers a household; one active registration per property.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="application">The means test.</param>
    /// <param name="maximumIncome">The income threshold households must be at or under.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The registration id, or why it was refused.</returns>
    Task<Result<long>> RegisterAsync(long companyId, IndigentApplication application, decimal maximumIncome, CancellationToken ct = default);

    /// <summary>Records a review of a registration's means test.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="registrationId">The registration.</param>
    /// <param name="householdIncome">The income found.</param>
    /// <param name="subsidyPercent">The subsidy share.</param>
    /// <param name="reviewDue">The next review date.</param>
    /// <param name="maximumIncome">The income threshold; above it the registration is revoked.</param>
    /// <param name="userId">Who reviewed it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether it still qualifies, or why the review was refused.</returns>
    Task<Result<bool>> ReviewAsync(long companyId, long registrationId, decimal householdIncome, decimal subsidyPercent, DateOnly reviewDue, decimal maximumIncome, long userId, CancellationToken ct = default);

    /// <summary>
    /// Credits each registered household its subsidy share of the month's bills, once per property per month, in one unit.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="monthEnd">Any date in the month; credits are dated its last day.</param>
    /// <param name="subsidyAccountCode">The indigent subsidy expense.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subsidies credited, or why the run failed.</returns>
    Task<Result<IReadOnlyList<SubsidyLine>>> ApplySubsidiesAsync(long companyId, DateOnly monthEnd, string subsidyAccountCode, CancellationToken ct = default);

    /// <summary>Registrations whose means test is overdue or due within a number of days.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="withinDays">How far ahead to look.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reviews due, soonest first.</returns>
    Task<IReadOnlyList<IndigentReviewDue>> ReviewsDueAsync(long companyId, DateOnly asAtDate, int withinDays, CancellationToken ct = default);
}
