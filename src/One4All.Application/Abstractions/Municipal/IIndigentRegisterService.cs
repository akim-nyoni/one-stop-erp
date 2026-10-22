using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A household's means test, as keyed by the user requesting its registration.</summary>
/// <param name="PropertyId">The property.</param>
/// <param name="HouseholdIncome">Monthly household income.</param>
/// <param name="SubsidyPercent">The share of bills to subsidise.</param>
/// <param name="ApprovedFrom">The first day subsidised.</param>
/// <param name="ReviewDue">When the means test must be reviewed.</param>
/// <param name="RequestedByUserId">Who keys it (another user approves it).</param>
public sealed record IndigentApplication(long PropertyId, decimal HouseholdIncome, decimal SubsidyPercent, DateOnly ApprovedFrom, DateOnly ReviewDue, long RequestedByUserId);

/// <summary>One property's subsidy in a run, in one currency.</summary>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">Its stand number.</param>
/// <param name="Currency">The currency the charges were billed, and the subsidy credited, in.</param>
/// <param name="Billed">The eligible charges billed in the month in that currency (interest and penalties left out).</param>
/// <param name="Subsidy">The subsidy credited.</param>
public sealed record SubsidyLine(long PropertyId, string StandNumber, CurrencyCode Currency, decimal Billed, decimal Subsidy);

/// <summary>A registration whose means test is due for review.</summary>
/// <param name="RegistrationId">The registration.</param>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">Its stand number.</param>
/// <param name="ReviewDue">When the review is due.</param>
/// <param name="Overdue">Whether the date has passed (and subsidy stopped).</param>
public sealed record IndigentReviewDue(long RegistrationId, long PropertyId, string StandNumber, DateOnly ReviewDue, bool Overdue);

/// <summary>A registration requested and awaiting another user's approval.</summary>
/// <param name="RegistrationId">The registration.</param>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">Its stand number.</param>
/// <param name="HouseholdIncome">The income found on the means test.</param>
/// <param name="SubsidyPercent">The subsidy share asked for.</param>
/// <param name="ApprovedFrom">The first day it would be subsidised.</param>
/// <param name="RequestedByUserId">Who keyed it.</param>
public sealed record PendingIndigentRegistration(long RegistrationId, long PropertyId, string StandNumber, decimal HouseholdIncome, decimal SubsidyPercent, DateOnly ApprovedFrom, long RequestedByUserId);

/// <summary>
/// The indigent / social-tariff register (brief §4.9): the council's qualifying income threshold held as a policy setting;
/// means-tested registrations keyed by one user and approved by another holding the indigent approve right, with review
/// dates; a monthly subsidy run crediting each registered household its subsidy share of the month's eligible charges
/// (interest and penalties left out), per currency in that currency (Dr indigent subsidy, Cr the ratepayer's account),
/// once per property per month; and the list of means tests due for review.
/// </summary>
public interface IIndigentRegisterService
{
    /// <summary>Sets the council's qualifying maximum monthly household income.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="maximumIncome">The threshold; zero or more.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetPolicyAsync(long companyId, decimal maximumIncome, CancellationToken ct = default);

    /// <summary>The council's qualifying maximum monthly household income, or null when it has not set one.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The threshold, or null.</returns>
    Task<decimal?> PolicyAsync(long companyId, CancellationToken ct = default);

    /// <summary>Requests a household's registration, measured against the council's threshold; one live registration per property.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="application">The means test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The registration id (pending approval), or why it was refused.</returns>
    Task<Result<long>> RequestAsync(long companyId, IndigentApplication application, CancellationToken ct = default);

    /// <summary>Approves a pending registration: by a user other than who requested it, holding Municipal.Indigent approve.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="registrationId">The registration.</param>
    /// <param name="approverUserId">Who approves it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, long registrationId, long approverUserId, CancellationToken ct = default);

    /// <summary>Turns a pending registration down, with the reason: by a user other than who requested it, holding Municipal.Indigent approve.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="registrationId">The registration.</param>
    /// <param name="userId">Who turns it down.</param>
    /// <param name="reason">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectAsync(long companyId, long registrationId, long userId, string reason, CancellationToken ct = default);

    /// <summary>The registrations awaiting approval, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending registrations.</returns>
    Task<IReadOnlyList<PendingIndigentRegistration>> PendingAsync(long companyId, CancellationToken ct = default);

    /// <summary>Records a review of a registration's means test against the council's threshold; above it the registration is revoked.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="registrationId">The registration.</param>
    /// <param name="householdIncome">The income found.</param>
    /// <param name="subsidyPercent">The subsidy share.</param>
    /// <param name="reviewDue">The next review date.</param>
    /// <param name="userId">Who reviewed it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether it still qualifies, or why the review was refused.</returns>
    Task<Result<bool>> ReviewAsync(long companyId, long registrationId, decimal householdIncome, decimal subsidyPercent, DateOnly reviewDue, long userId, CancellationToken ct = default);

    /// <summary>
    /// Credits each registered household its subsidy share of the month's eligible charges, per currency in that currency, once per
    /// property per month, in one unit. Finance charges (interest) and penalty charges are not subsidised.
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
