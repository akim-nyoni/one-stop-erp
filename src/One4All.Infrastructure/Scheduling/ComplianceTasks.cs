using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;

namespace One4All.Infrastructure.Scheduling;

// The periodic compliance runs (brief §4.5, §4.9, §4.10, §4.16): each is safe to run daily - a run that finds nothing to do does nothing.

/// <summary>Scheduled task: reviews payment arrangements (missed instalments past 7 days' grace; two missed defaults one) and promises to pay (key <c>ar.arrangements</c>).</summary>
public sealed class ArrangementReviewTask : IScheduledTask
{
    private readonly Application.Abstractions.Receipting.IArrangementMonitor _arrangements;
    private readonly Application.Abstractions.Trading.IPromiseToPayService _promises;

    /// <summary>Creates the task.</summary>
    /// <param name="arrangements">Reviews payment arrangements.</param>
    /// <param name="promises">Reviews promises to pay.</param>
    public ArrangementReviewTask(Application.Abstractions.Receipting.IArrangementMonitor arrangements, Application.Abstractions.Trading.IPromiseToPayService promises)
    {
        _arrangements = arrangements;
        _promises = promises;
    }

    /// <inheritdoc />
    public string Key => "ar.arrangements";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        var arrangements = await _arrangements.ReviewAsync(companyId, today, 7, 2, ct).ConfigureAwait(false);
        var promises = await _promises.ReviewAsync(companyId, today, ct).ConfigureAwait(false);
        return $"{arrangements.Count} arrangement(s) reviewed: {arrangements.Count(a => a.Standing.Defaulted)} defaulted, {arrangements.Count(a => a.Standing.Honoured)} honoured; "
            + $"promises to pay: {promises.Kept.Count} kept, {promises.Broken.Count} broken, {promises.StillOpen} still open.";
    }
}

/// <summary>Scheduled task: issues each housing instalment agreement's next default notice that is due and charges the month's penalty interest once (key <c>housing.instalment-defaults</c>).</summary>
public sealed class InstalmentDefaultsTask : IScheduledTask
{
    private readonly Application.Abstractions.Housing.IInstalmentDefaultService _defaults;

    /// <summary>Creates the task.</summary>
    /// <param name="defaults">The instalment default service.</param>
    public InstalmentDefaultsTask(Application.Abstractions.Housing.IInstalmentDefaultService defaults) => _defaults = defaults;

    /// <inheritdoc />
    public string Key => "housing.instalment-defaults";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        int issued = 0;
        foreach (var due in (await _defaults.ReviewAsync(companyId, today, ct).ConfigureAwait(false)).Where(c => c.NextStage is not null))
        {
            var notice = await _defaults.IssueNextAsync(companyId, due.AgreementNumber, today, ct).ConfigureAwait(false);
            if (notice.IsSuccess)
            {
                issued++;
            }
        }

        var penalty = await _defaults.ChargePenaltyInterestAsync(companyId, today, ct).ConfigureAwait(false);
        return penalty.IsFailure ? penalty.Error
            : $"{issued} default notice(s) issued; penalty interest {penalty.Value.Total:N2} on {penalty.Value.Agreements} agreement(s) for {today:MMMM yyyy}.";
    }
}

/// <summary>Scheduled task: flags development covenants past their deadline as breached (no levy is invoiced by the schedule) (key <c>housing.covenant-breaches</c>).</summary>
public sealed class CovenantBreachesTask : IScheduledTask
{
    private readonly Application.Abstractions.Housing.IDevelopmentCovenantService _covenants;

    /// <summary>Creates the task.</summary>
    /// <param name="covenants">The development covenant service.</param>
    public CovenantBreachesTask(Application.Abstractions.Housing.IDevelopmentCovenantService covenants) => _covenants = covenants;

    /// <inheritdoc />
    public string Key => "housing.covenant-breaches";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        var breached = await _covenants.RunBreachesAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 0m, string.Empty, ct).ConfigureAwait(false);
        return breached.IsFailure ? breached.Error
            : breached.Value.Count == 0 ? "No development covenant is past its deadline." : $"{breached.Value.Count} covenant(s) breached: {string.Join(", ", breached.Value)}.";
    }
}

/// <summary>Scheduled task: lists indigent registrations whose review falls due within 30 days or is overdue (subsidy stopped) (key <c>municipal.indigent-reviews</c>).</summary>
public sealed class IndigentReviewsTask : IScheduledTask
{
    private readonly Application.Abstractions.Municipal.IIndigentRegisterService _indigent;

    /// <summary>Creates the task.</summary>
    /// <param name="indigent">The indigent register.</param>
    public IndigentReviewsTask(Application.Abstractions.Municipal.IIndigentRegisterService indigent) => _indigent = indigent;

    /// <inheritdoc />
    public string Key => "municipal.indigent-reviews";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        var due = await _indigent.ReviewsDueAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 30, ct).ConfigureAwait(false);
        return due.Count == 0 ? "No indigent review is due within 30 days."
            : $"{due.Count} indigent review(s) due within 30 days, {due.Count(d => d.Overdue)} overdue (subsidy stopped): {string.Join(", ", due.Take(20).Select(d => d.StandNumber))}.";
    }
}

/// <summary>Scheduled task: lists council licences expiring within 30 days or already expired (key <c>municipal.licence-renewals</c>).</summary>
public sealed class LicenceRenewalsTask : IScheduledTask
{
    private readonly Application.Abstractions.Municipal.ICouncilLicensingService _licences;

    /// <summary>Creates the task.</summary>
    /// <param name="licences">The council licensing service.</param>
    public LicenceRenewalsTask(Application.Abstractions.Municipal.ICouncilLicensingService licences) => _licences = licences;

    /// <inheritdoc />
    public string Key => "municipal.licence-renewals";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        var due = await _licences.RenewalsDueAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 30, ct).ConfigureAwait(false);
        return due.Count == 0 ? "No council licence expires within 30 days."
            : $"{due.Count} licence(s) to renew, {due.Count(l => l.DaysLeft < 0)} already expired: {string.Join(", ", due.Take(20).Select(l => l.LicenceNumber))}.";
    }
}

/// <summary>Scheduled task: lists student bursaries and scholarships ending within 30 days (key <c>schools.bursary-expiry</c>).</summary>
public sealed class BursaryExpiryTask : IScheduledTask
{
    private readonly Application.Abstractions.Schools.ISchoolBursaryService _bursaries;

    /// <summary>Creates the task.</summary>
    /// <param name="bursaries">The bursary service.</param>
    public BursaryExpiryTask(Application.Abstractions.Schools.ISchoolBursaryService bursaries) => _bursaries = bursaries;

    /// <inheritdoc />
    public string Key => "schools.bursary-expiry";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        var ending = await _bursaries.ExpiringAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 30, ct).ConfigureAwait(false);
        return ending.Count == 0 ? "No bursary ends within 30 days."
            : $"{ending.Count} bursary award(s) ending within 30 days: {string.Join(", ", ending.Take(20).Select(b => $"{b.AdmissionNumber} {b.StudentName} ({b.ValidUntil:yyyy-MM-dd})"))}.";
    }
}
