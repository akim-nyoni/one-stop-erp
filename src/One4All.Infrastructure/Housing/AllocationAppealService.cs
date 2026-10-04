using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The allocation appeals register.</summary>
public sealed class AllocationAppealService : IAllocationAppealService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    public AllocationAppealService(One4AllDbContext db, INumberSequenceService numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<Result<string>> LodgeAsync(long companyId, string waitingListNumber, string runNumber, string grounds, DateOnly lodgedOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(grounds))
        {
            return Error.Validation("Hse.AppealGrounds", "An appeal needs its grounds.");
        }

        string wl = (waitingListNumber ?? string.Empty).Trim();
        HousingApplicant? applicant = await _db.HousingApplicants.AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.WaitingListNumber == wl, ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return Error.NotFound("Hse.ApplicantNotFound", $"No waiting-list registration {wl}.");
        }

        string run = (runNumber ?? string.Empty).Trim();
        AllocationRun? allocationRun = await _db.Set<AllocationRun>().AsNoTracking().Include(r => r.Allocations)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber == run, ct).ConfigureAwait(false);
        if (allocationRun is null)
        {
            return Error.NotFound("Hse.RunNotFound", $"Allocation run {run} not found.");
        }

        if (allocationRun.Status != AllocationRunStatus.Approved)
        {
            return Error.Rule("Hse.RunNotApproved", $"Only an approved run can be appealed; {run} is {allocationRun.Status}.");
        }

        if (allocationRun.Allocations.Any(a => a.ApplicantId == applicant.Id))
        {
            return Error.Rule("Hse.NotUnsuccessful", $"Applicant {wl} was allocated in {run}.");
        }

        if (await _db.Set<AllocationAppeal>().AnyAsync(a => a.ApplicantId == applicant.Id && a.AllocationRunId == allocationRun.Id, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.AppealExists", $"Applicant {wl} has already appealed against {run}.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "APL", lodgedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<AllocationAppeal>().Add(new AllocationAppeal(companyId, number.Value, applicant.Id, allocationRun.Id, grounds.Trim(), lodgedOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> SetHearingAsync(long companyId, string appealNumber, DateOnly hearingDate, CancellationToken ct = default)
    {
        Result<AllocationAppeal> found = await FindAsync(companyId, appealNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        AllocationAppeal appeal = found.Value;
        if (appeal.Status is not (AppealStatus.Lodged or AppealStatus.HearingSet))
        {
            return Result.Failure(Error.Rule("Hse.AppealDecided", $"Appeal {appealNumber} has been decided."));
        }

        if (hearingDate < appeal.LodgedOn)
        {
            return Result.Failure(Error.Validation("Hse.HearingDate", "The hearing cannot be before the appeal was lodged."));
        }

        appeal.SetHearing(hearingDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DecideAsync(long companyId, string appealNumber, bool upheld, DateOnly date, string reasons, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasons))
        {
            return Result.Failure(Error.Validation("Hse.AppealReasons", "Record the reasons for the outcome."));
        }

        Result<AllocationAppeal> found = await FindAsync(companyId, appealNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        AllocationAppeal appeal = found.Value;
        if (appeal.Status != AppealStatus.HearingSet)
        {
            return Result.Failure(Error.Rule("Hse.NoHearing", $"Appeal {appealNumber} must be heard before it is decided."));
        }

        if (date < appeal.HearingDate)
        {
            return Result.Failure(Error.Validation("Hse.DecisionDate", "The decision cannot be before the hearing."));
        }

        appeal.Decide(upheld, date, reasons.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> LinkReallocationAsync(long companyId, string appealNumber, string certificateNumber, CancellationToken ct = default)
    {
        Result<AllocationAppeal> found = await FindAsync(companyId, appealNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        AllocationAppeal appeal = found.Value;
        if (appeal.Status != AppealStatus.Upheld)
        {
            return Result.Failure(Error.Rule("Hse.AppealNotUpheld", $"Only an upheld appeal leads to a re-allocation; {appealNumber} is {appeal.Status}."));
        }

        string certificate = (certificateNumber ?? string.Empty).Trim();
        bool issuedToAppellant = await (
            from a in _db.Set<StandAllocation>()
            join r in _db.Set<AllocationRun>() on a.AllocationRunId equals r.Id
            where r.CompanyId == companyId && a.CertificateNumber == certificate && a.ApplicantId == appeal.ApplicantId
            select a.Id).AnyAsync(ct).ConfigureAwait(false);
        if (!issuedToAppellant)
        {
            return Result.Failure(Error.Rule("Hse.CertificateNotAppellants", $"Certificate {certificate} was not issued to the appellant."));
        }

        appeal.LinkReallocation(certificate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppealEntry>> RegisterAsync(long companyId, CancellationToken ct = default)
        => await (
            from ap in _db.Set<AllocationAppeal>().AsNoTracking()
            join a in _db.HousingApplicants.AsNoTracking() on ap.ApplicantId equals a.Id
            join r in _db.Set<AllocationRun>().AsNoTracking() on ap.AllocationRunId equals r.Id
            where ap.CompanyId == companyId
            orderby ap.LodgedOn, ap.AppealNumber
            select new AppealEntry(ap.AppealNumber, a.WaitingListNumber ?? string.Empty, r.RunNumber, ap.LodgedOn, ap.Status, ap.HearingDate, ap.ReallocationCertificate))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<Result<AllocationAppeal>> FindAsync(long companyId, string appealNumber, CancellationToken ct)
    {
        string number = (appealNumber ?? string.Empty).Trim();
        AllocationAppeal? appeal = await _db.Set<AllocationAppeal>().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AppealNumber == number, ct).ConfigureAwait(false);
        if (appeal is null)
        {
            return Error.NotFound("Hse.AppealNotFound", $"Appeal {number} not found.");
        }

        return appeal;
    }
}
