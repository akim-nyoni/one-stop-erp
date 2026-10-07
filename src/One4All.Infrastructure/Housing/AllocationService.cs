using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Stand allocation runs: proposed lists, dual-authorised approval, offers and draw verification.</summary>
public sealed class AllocationService : IAllocationService
{
    private static readonly AllocationStatus[] Live = [AllocationStatus.Proposed, AllocationStatus.Offered, AllocationStatus.Accepted];

    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IWaitingListService _waitingList;
    private readonly IHousingMunicipalLinkService _municipalLink;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="waitingList">The waiting list, for rankings.</param>
    /// <param name="municipalLink">The link to the municipal register.</param>
    public AllocationService(One4AllDbContext db, INumberSequenceService numbers, IWaitingListService waitingList, IHousingMunicipalLinkService municipalLink)
    {
        _db = db;
        _numbers = numbers;
        _waitingList = waitingList;
        _municipalLink = municipalLink;
    }

    /// <inheritdoc />
    public Task<Result<AllocationRunView>> ProposeAsync(AllocationProposal proposal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return _db.AtomicAsync(() => ProposeCoreAsync(proposal, ct), ct);
    }

    private async Task<Result<AllocationRunView>> ProposeCoreAsync(AllocationProposal proposal, CancellationToken ct)
    {
        if (!await _db.Set<HousingScheme>().AnyAsync(s => s.Id == proposal.SchemeId && s.CompanyId == proposal.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.SchemeNotFound", $"Scheme {proposal.SchemeId} not found.");
        }

        // Free stands exclude every route a stand can be blocked or promised through, so an approval does not then trip over one.
        HashSet<long> unavailable = await StandClaims.UnavailableAsync(_db, proposal.CompanyId, ct).ConfigureAwait(false);
        HashSet<long> liveApplicants = [.. await _db.Set<StandAllocation>().Where(a => a.CompanyId == proposal.CompanyId && Live.Contains(a.Status)).Select(a => a.ApplicantId).ToListAsync(ct).ConfigureAwait(false)];
        var stands = (await _db.Set<HousingStand>().AsNoTracking()
                .Where(s => s.CompanyId == proposal.CompanyId && s.SchemeId == proposal.SchemeId && s.Status == StandStatus.Available)
                .OrderBy(s => s.StandNumber).Select(s => new { s.Id, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Where(s => !unavailable.Contains(s.Id)).ToList();

        var listed = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == proposal.CompanyId && a.Category == proposal.Category && a.WaitingListNumber != null && (a.Status == ApplicantStatus.Active || a.Status == ApplicantStatus.Suspended))
            .Select(a => new { a.Id, Number = a.WaitingListNumber!, a.Status, a.IdNumber, a.SpouseIdNumber }).ToListAsync(ct).ConfigureAwait(false);

        // One household, one stand: whoever's spouse holds a stand, or is an applicant with a live or completed allocation, is left out.
        HashSet<string> householdIds = [.. listed.SelectMany(a => new[] { a.IdNumber, a.SpouseIdNumber }).OfType<string>()];
        HashSet<string> holdingIds = [.. await _db.Customers.AsNoTracking()
            .Where(c => c.CompanyId == proposal.CompanyId && c.NationalIdNumber != null && householdIds.Contains(c.NationalIdNumber)
                && _db.HousingStands.Any(s => s.CompanyId == proposal.CompanyId && s.CurrentHolderCustomerId == c.Id))
            .Select(c => c.NationalIdNumber!).ToListAsync(ct).ConfigureAwait(false)];
        holdingIds.UnionWith(await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == proposal.CompanyId && householdIds.Contains(a.IdNumber)
                && (a.Status == ApplicantStatus.Allocated || _db.Set<StandAllocation>().Any(x => x.ApplicantId == a.Id && Live.Contains(x.Status))))
            .Select(a => a.IdNumber).ToListAsync(ct).ConfigureAwait(false));
        HashSet<long> spouseHolds = [.. listed.Where(a => (a.SpouseIdNumber is string spouse && holdingIds.Contains(spouse))
            || listed.Any(o => o.SpouseIdNumber == a.IdNumber && liveApplicants.Contains(o.Id))).Select(a => a.Id)];
        var applicants = listed.Where(a => a.Status == ApplicantStatus.Active && !liveApplicants.Contains(a.Id) && !spouseHolds.Contains(a.Id)).ToList();
        Dictionary<string, long> applicantIds = applicants.ToDictionary(a => a.Number, a => a.Id, StringComparer.Ordinal);

        // The canonical candidate list: ranked for seniority and score, waiting-list-number order for a ballot.
        List<string> canonical;
        if (proposal.Method == AllocationMethod.Ballot)
        {
            canonical = [.. applicantIds.Keys.Order(StringComparer.Ordinal)];
        }
        else
        {
            WaitingListOrder order = proposal.Method == AllocationMethod.Score ? WaitingListOrder.Score : WaitingListOrder.Seniority;
            canonical = [.. (await _waitingList.RankingAsync(proposal.CompanyId, proposal.Category, order, proposal.AsAtDate, ct).ConfigureAwait(false))
                .Select(p => p.WaitingListNumber).Where(applicantIds.ContainsKey)];
        }

        if (stands.Count == 0 || canonical.Count == 0)
        {
            return Error.Rule("Hse.NothingToAllocate", "There are no available stands or no eligible applicants for this run.");
        }

        long? seed = proposal.Method == AllocationMethod.Ballot ? proposal.Seed ?? BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) : null;
        List<string> standNumbers = [.. stands.Select(s => s.StandNumber)];
        IReadOnlyList<string> drawn = seed is long s0 ? AllocationDraw.Ballot(canonical, s0) : canonical;
        string hash = AllocationDraw.InputHash(canonical, standNumbers);

        Result<string> number = await _numbers.AllocateAsync(proposal.CompanyId, "ALR", proposal.AsAtDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        AllocationRun run = new(proposal.CompanyId, number.Value, proposal.SchemeId, proposal.Category, proposal.Method, seed,
            string.Join('|', canonical), string.Join('|', standNumbers), hash, proposal.UserId);
        Dictionary<string, long> standIds = stands.ToDictionary(s => s.StandNumber, s => s.Id, StringComparer.Ordinal);
        int sequence = 0;
        foreach ((string stand, string candidate) in AllocationDraw.Pair(standNumbers, drawn))
        {
            run.Add(new StandAllocation(++sequence, standIds[stand], applicantIds[candidate]));
        }

        foreach (var left in listed.Where(a => !applicantIds.ContainsKey(a.Number)).OrderBy(a => a.Number, StringComparer.Ordinal))
        {
            run.Exclude(left.Id, left.Status == ApplicantStatus.Suspended ? ExclusionReason.Suspended
                : spouseHolds.Contains(left.Id) && !liveApplicants.Contains(left.Id) ? ExclusionReason.SpouseHolds : ExclusionReason.LiveAllocation);
        }

        _db.Set<AllocationRun>().Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ViewAsync(run, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<AllocationRunView>> ApproveAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, runNumber, approverUserId, date, offerDays, ct), ct);

    private async Task<Result<AllocationRunView>> ApproveCoreAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CancellationToken ct)
    {
        if (offerDays < 1)
        {
            return Error.Validation("Hse.OfferDays", "Offers must stay open for at least a day.");
        }

        Result<AllocationRun> found = await FindRunAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        AllocationRun run = found.Value;
        if (run.Status != AllocationRunStatus.Proposed)
        {
            return Error.Rule("Hse.RunNotProposed", $"Run {runNumber} is {run.Status}.");
        }

        if (run.ProposedByUserId == approverUserId)
        {
            return Error.Rule("Hse.SelfApproval", $"Run {runNumber} must be approved by someone other than who proposed it.");
        }

        List<long> standIds = [.. run.Allocations.Select(a => a.StandId)];
        Dictionary<long, HousingStand> stands = await _db.Set<HousingStand>().Where(s => standIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        HousingStand? gone = stands.Values.FirstOrDefault(s => s.Status != StandStatus.Available);
        if (gone is not null)
        {
            return Error.Conflict("Hse.StandUnavailable", $"Stand {gone.StandNumber} is no longer available; cancel the run and propose again.");
        }

        if (await StandClaims.AnyBlockedAsync(_db, standIds, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.StandBlocked", "A stand in the run has since been blocked; cancel the run and propose again.");
        }

        foreach (StandAllocation allocation in run.Allocations.OrderBy(a => a.Sequence))
        {
            Result<string> certificate = await _numbers.AllocateAsync(companyId, "AC", date, ct: ct).ConfigureAwait(false);
            if (certificate.IsFailure)
            {
                return certificate.Error;
            }

            // The beneficiary's price is frozen at the offer date (brief §4.10.2); later price changes do not touch it.
            StandPriceQuote? price = await StandPrices.QuoteAsync(_db, stands[allocation.StandId], date, ct).ConfigureAwait(false);
            allocation.Offer(certificate.Value, date.AddDays(offerDays), price?.BuildUp.Price);
            stands[allocation.StandId].MarkAllocated();
        }

        run.Approve(approverUserId, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ViewAsync(run, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        Result<AllocationRun> found = await FindRunAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status != AllocationRunStatus.Proposed)
        {
            return Result.Failure(Error.Rule("Hse.RunNotProposed", $"Run {runNumber} is {found.Value.Status}."));
        }

        found.Value.Cancel();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> AcceptAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => AcceptCoreAsync(companyId, certificateNumber, date, ct), ct);

    private async Task<Result> AcceptCoreAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct)
    {
        Result<StandAllocation> found = await FindOfferAsync(companyId, certificateNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        StandAllocation allocation = found.Value;
        if (date > allocation.OfferExpires)
        {
            return Result.Failure(Error.Rule("Hse.OfferExpired", $"The offer under {certificateNumber} expired on {allocation.OfferExpires:yyyy-MM-dd}."));
        }

        HousingApplicant applicant = await _db.HousingApplicants.FirstAsync(a => a.Id == allocation.ApplicantId, ct).ConfigureAwait(false);
        allocation.Accept(date);
        applicant.SetStatus(ApplicantStatus.Allocated);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Where the council links stands to the municipal register on acceptance, the beneficiary's account becomes the ratepayer.
        long? beneficiary = await _db.Customers.Where(c => c.CompanyId == companyId && c.NationalIdNumber == applicant.IdNumber).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (beneficiary is long customerId)
        {
            Result<MunicipalLinkResult> linked = await _municipalLink.OnEventAsync(companyId, allocation.StandId, customerId, MunicipalLinkTrigger.AllocationAccepted, date, null, ct).ConfigureAwait(false);
            if (linked.IsFailure)
            {
                return Result.Failure(linked.Error);
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeclineAsync(long companyId, string certificateNumber, DateOnly date, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Hse.DeclineReason", "Record why the offer was declined."));
        }

        Result<StandAllocation> found = await FindOfferAsync(companyId, certificateNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        await ReleaseAsync(found.Value, AllocationStatus.Declined, date, reason.Trim(), ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> LapseExpiredAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        List<StandAllocation> expired = await (
            from a in _db.Set<StandAllocation>()
            join r in _db.Set<AllocationRun>() on a.AllocationRunId equals r.Id
            where r.CompanyId == companyId && a.Status == AllocationStatus.Offered && a.OfferExpires < asAtDate
            orderby a.CertificateNumber
            select a).ToListAsync(ct).ConfigureAwait(false);
        foreach (StandAllocation allocation in expired)
        {
            await ReleaseAsync(allocation, AllocationStatus.Lapsed, asAtDate, "Offer not accepted by the deadline", ct).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return [.. expired.Select(a => a.CertificateNumber!)];
    }

    /// <inheritdoc />
    public async Task<Result<DrawVerification>> VerifyDrawAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        Result<AllocationRun> found = await FindRunAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        AllocationRun run = found.Value;
        string[] candidates = run.InputCandidates.Length == 0 ? [] : run.InputCandidates.Split('|');
        string[] stands = run.InputStands.Length == 0 ? [] : run.InputStands.Split('|');
        bool hashMatches = AllocationDraw.InputHash(candidates, stands) == run.InputHash;
        IReadOnlyList<string> drawn = run.Method == AllocationMethod.Ballot && run.Seed is long seed ? AllocationDraw.Ballot(candidates, seed) : candidates;
        AllocationRunView view = await ViewAsync(run, ct).ConfigureAwait(false);
        bool orderMatches = AllocationDraw.Pair(stands, drawn).SequenceEqual(view.Lines.OrderBy(l => l.Sequence).Select(l => (l.StandNumber, l.WaitingListNumber)));
        return new DrawVerification(hashMatches, orderMatches);
    }

    private async Task ReleaseAsync(StandAllocation allocation, AllocationStatus status, DateOnly date, string reason, CancellationToken ct)
    {
        HousingStand stand = await _db.Set<HousingStand>().FirstAsync(s => s.Id == allocation.StandId, ct).ConfigureAwait(false);
        allocation.Close(status, date, reason);
        stand.ReleaseAllocation();
    }

    private async Task<Result<AllocationRun>> FindRunAsync(long companyId, string runNumber, CancellationToken ct)
    {
        string number = (runNumber ?? string.Empty).Trim();
        AllocationRun? run = await _db.Set<AllocationRun>().Include(r => r.Allocations)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber == number, ct).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound("Hse.RunNotFound", $"Allocation run {number} not found.");
        }

        return run;
    }

    private async Task<Result<StandAllocation>> FindOfferAsync(long companyId, string certificateNumber, CancellationToken ct)
    {
        string number = (certificateNumber ?? string.Empty).Trim();
        StandAllocation? allocation = await (
            from a in _db.Set<StandAllocation>()
            join r in _db.Set<AllocationRun>() on a.AllocationRunId equals r.Id
            where r.CompanyId == companyId && a.CertificateNumber == number
            select a).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (allocation is null)
        {
            return Error.NotFound("Hse.CertificateNotFound", $"Allocation certificate {number} not found.");
        }

        if (allocation.Status != AllocationStatus.Offered)
        {
            return Error.Rule("Hse.OfferNotOpen", $"The offer under {number} is {allocation.Status}.");
        }

        return allocation;
    }

    private async Task<AllocationRunView> ViewAsync(AllocationRun run, CancellationToken ct)
    {
        List<long> standIds = [.. run.Allocations.Select(a => a.StandId)];
        List<long> applicantIds = [.. run.Allocations.Select(a => a.ApplicantId)];
        Dictionary<long, string> stands = await _db.Set<HousingStand>().AsNoTracking().Where(s => standIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.StandNumber, ct).ConfigureAwait(false);
        Dictionary<long, string> applicants = await _db.HousingApplicants.AsNoTracking().Where(a => applicantIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.WaitingListNumber ?? string.Empty, ct).ConfigureAwait(false);
        List<AllocationLine> lines = [.. run.Allocations.OrderBy(a => a.Sequence)
            .Select(a => new AllocationLine(a.Sequence, stands[a.StandId], applicants[a.ApplicantId], a.Status, a.CertificateNumber, a.OfferedPrice))];
        return new AllocationRunView(run.RunNumber, run.Method, run.Seed, run.InputHash, run.Status, lines);
    }
}
