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
    public WaitingListControlService(One4AllDbContext db, Application.Abstractions.Communications.INotificationService? notifications = null)
    {
        _db = db;
        _notifications = notifications;
    }

    private readonly Application.Abstractions.Communications.INotificationService? _notifications;

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
        List<long> ids = [.. listed.Select(a => a.Id)];
        var notices = await _db.Set<ApplicantNotice>().AsNoTracking().Where(n => n.CompanyId == companyId && ids.Contains(n.ApplicantId)).Select(n => new { n.ApplicantId, n.NoticeType, n.SentOn })
            .ToListAsync(ct).ConfigureAwait(false);
        string council = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        int suspended = 0, removed = 0, reminded = 0, byLetter = 0;
        foreach (HousingApplicant applicant in listed)
        {
            DateOnly confirmed = applicant.LastConfirmedOn ?? applicant.RegistrationDate;
            DateOnly? suspendedOn = notices.Where(n => n.ApplicantId == applicant.Id && n.NoticeType == ApplicantNoticeType.Suspended && n.SentOn >= confirmed).Select(n => (DateOnly?)n.SentOn).Max();
            bool remindedThisCycle = notices.Any(n => n.ApplicantId == applicant.Id && n.NoticeType == ApplicantNoticeType.RenewalDue && n.SentOn >= confirmed);
            RenewalAction action = WaitingListRenewal.Assess(applicant.Status, confirmed, asAtDate, Math.Max(1, cycleMonths), Math.Max(0, graceDays), suspendedOn, remindedThisCycle);
            DateOnly due = confirmed.AddMonths(Math.Max(1, cycleMonths));
            (ApplicantNoticeType type, string text)? notice = action switch
            {
                RenewalAction.Remind => (ApplicantNoticeType.RenewalDue,
                    $"{council}: your housing waiting-list registration {applicant.WaitingListNumber} must be renewed by {due:yyyy-MM-dd}. Visit or call the housing office to keep your place."),
                RenewalAction.Suspend => (ApplicantNoticeType.Suspended,
                    $"{council}: your housing waiting-list registration {applicant.WaitingListNumber} was not renewed by {due:yyyy-MM-dd} and is suspended. Renew within {Math.Max(0, graceDays)} days or it will be removed."),
                RenewalAction.Remove => (ApplicantNoticeType.Removed,
                    $"{council}: your housing waiting-list registration {applicant.WaitingListNumber} has been removed because it was not renewed. Contact the housing office if this is wrong."),
                _ => null,
            };
            if (notice is not (ApplicantNoticeType type, string text))
            {
                continue;
            }

            if (action == RenewalAction.Suspend)
            {
                applicant.SetStatus(ApplicantStatus.Suspended);
                suspended++;
            }
            else if (action == RenewalAction.Remove)
            {
                applicant.SetStatus(ApplicantStatus.Removed);
                removed++;
            }
            else
            {
                reminded++;
            }

            // Told by SMS when there is a number; otherwise a letter to the address on file - either way the notice records where it went.
            string? phone = string.IsNullOrWhiteSpace(applicant.Phone) ? null : applicant.Phone.Trim();
            if (phone is not null && _notifications is not null)
            {
                _ = await _notifications.QueueDirectAsync(companyId, $"Waiting list {type} {applicant.WaitingListNumber}", Core.Communications.MessageChannel.Sms, [phone],
                    "Housing waiting list", text, null, ct).ConfigureAwait(false);
            }
            else
            {
                byLetter++;
            }

            _db.Set<ApplicantNotice>().Add(new ApplicantNotice(companyId, applicant.Id, type, asAtDate)
            {
                Channel = phone is not null ? "SMS" : "Letter",
                Recipient = phone ?? applicant.PostalAddress ?? applicant.PhysicalAddress ?? "(no address on file)",
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RenewalRunResult(suspended, removed, reminded, byLetter);
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
            select new ApplicantNoticeEntry(n.NoticeType, n.SentOn, n.Channel, n.Recipient))
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
