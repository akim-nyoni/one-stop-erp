using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Waiting-list integrity: dual-authorised date changes, renewals and the notice trail.</summary>
public sealed class WaitingListControlService : IWaitingListControlService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public WaitingListControlService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> RequestDateChangeAsync(long companyId, string waitingListNumber, DateOnly newDate, string reason, long userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Hse.DateChangeReason", "A registration-date change needs a reason.");
        }

        Result<HousingApplicant> found = await FindAsync(companyId, waitingListNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        HousingApplicant applicant = found.Value;
        if (newDate == applicant.RegistrationDate)
        {
            return Error.Validation("Hse.DateUnchanged", "The new date is the current registration date.");
        }

        if (await _db.Set<RegistrationDateChange>().AnyAsync(c => c.ApplicantId == applicant.Id && c.Status == ChangeRequestStatus.Pending, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.DateChangePending", $"Applicant {waitingListNumber} already has a date change awaiting approval.");
        }

        RegistrationDateChange change = new(companyId, applicant.Id, applicant.RegistrationDate, newDate, reason.Trim(), userId);
        _db.Set<RegistrationDateChange>().Add(change);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return change.Id;
    }

    /// <inheritdoc />
    public async Task<Result> DecideDateChangeAsync(long companyId, long requestId, bool approve, long userId, CancellationToken ct = default)
    {
        RegistrationDateChange? change = await _db.Set<RegistrationDateChange>().FirstOrDefaultAsync(c => c.Id == requestId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Result.Failure(Error.NotFound("Hse.DateChangeNotFound", $"Date change {requestId} not found."));
        }

        if (change.Status != ChangeRequestStatus.Pending)
        {
            return Result.Failure(Error.Rule("Hse.DateChangeDecided", $"Date change {requestId} has already been {change.Status}."));
        }

        if (change.RequestedByUserId == userId)
        {
            return Result.Failure(Error.Rule("Hse.SelfApproval", "A registration-date change must be decided by someone other than who asked for it."));
        }

        change.Decide(approve, userId);
        if (approve)
        {
            HousingApplicant applicant = await _db.HousingApplicants.FirstAsync(a => a.Id == change.ApplicantId, ct).ConfigureAwait(false);
            applicant.ChangeRegistrationDate(change);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ConfirmAsync(long companyId, string waitingListNumber, DateOnly date, CancellationToken ct = default)
    {
        Result<HousingApplicant> found = await FindAsync(companyId, waitingListNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status is not (ApplicantStatus.Active or ApplicantStatus.Suspended))
        {
            return Result.Failure(Error.Rule("Hse.NotOnList", $"Applicant {waitingListNumber} is {found.Value.Status} and cannot renew."));
        }

        found.Value.Confirm(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<RenewalRunResult> RunRenewalsAsync(long companyId, DateOnly asAtDate, int cycleMonths, int graceDays, CancellationToken ct = default)
    {
        List<HousingApplicant> listed = await _db.HousingApplicants
            .Where(a => a.CompanyId == companyId && a.WaitingListNumber != null && (a.Status == ApplicantStatus.Active || a.Status == ApplicantStatus.Suspended))
            .ToListAsync(ct).ConfigureAwait(false);
        int suspended = 0;
        int removed = 0;
        foreach (HousingApplicant applicant in listed)
        {
            RenewalAction action = WaitingListRenewal.Assess(applicant.Status, applicant.LastConfirmedOn ?? applicant.RegistrationDate, asAtDate, Math.Max(1, cycleMonths), Math.Max(0, graceDays));
            if (action == RenewalAction.Suspend)
            {
                applicant.SetStatus(ApplicantStatus.Suspended);
                _db.Set<ApplicantNotice>().Add(new ApplicantNotice(companyId, applicant.Id, ApplicantNoticeType.Suspended, asAtDate));
                suspended++;
            }
            else if (action == RenewalAction.Remove)
            {
                applicant.SetStatus(ApplicantStatus.Removed);
                _db.Set<ApplicantNotice>().Add(new ApplicantNotice(companyId, applicant.Id, ApplicantNoticeType.Removed, asAtDate));
                removed++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RenewalRunResult(suspended, removed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicantNoticeEntry>> NoticesAsync(long companyId, string waitingListNumber, CancellationToken ct = default)
    {
        string number = (waitingListNumber ?? string.Empty).Trim();
        return await (
            from n in _db.Set<ApplicantNotice>().AsNoTracking()
            join a in _db.HousingApplicants.AsNoTracking() on n.ApplicantId equals a.Id
            where n.CompanyId == companyId && a.WaitingListNumber == number
            orderby n.SentOn, n.Id
            select new ApplicantNoticeEntry(n.NoticeType, n.SentOn))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<Result<HousingApplicant>> FindAsync(long companyId, string waitingListNumber, CancellationToken ct)
    {
        string number = (waitingListNumber ?? string.Empty).Trim();
        HousingApplicant? applicant = await _db.HousingApplicants.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.WaitingListNumber == number, ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return Error.NotFound("Hse.ApplicantNotFound", $"No waiting-list registration {number}.");
        }

        return applicant;
    }
}
