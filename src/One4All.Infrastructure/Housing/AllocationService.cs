using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Workflow;
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
    /// <param name="workflow">Routes a proposed run through the approval workflow when one is defined for allocation runs.</param>
    public AllocationService(One4AllDbContext db, INumberSequenceService numbers, IWaitingListService waitingList, IHousingMunicipalLinkService municipalLink,
        Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _numbers = numbers;
        _waitingList = waitingList;
        _municipalLink = municipalLink;
        _workflow = workflow;
    }

    private readonly Lazy<IWorkflowService>? _workflow;

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

        // A substitution run offers only the stands named (declined or lapsed offers) to the next on the list.
        List<string> onlyStands = [.. proposal.StandNumbers.Select(s => s.Trim()).Where(s => s.Length > 0)];

        // Free stands exclude every route a stand can be blocked or promised through, so an approval does not then trip over one.
        HashSet<long> unavailable = await StandClaims.UnavailableAsync(_db, proposal.CompanyId, ct).ConfigureAwait(false);
        HashSet<long> liveApplicants = [.. await _db.Set<StandAllocation>().Where(a => a.CompanyId == proposal.CompanyId && Live.Contains(a.Status)).Select(a => a.ApplicantId).ToListAsync(ct).ConfigureAwait(false)];
        var stands = (await _db.Set<HousingStand>().AsNoTracking()
                .Where(s => s.CompanyId == proposal.CompanyId && s.SchemeId == proposal.SchemeId && s.Status == StandStatus.Available
                    && (onlyStands.Count == 0 || onlyStands.Contains(s.StandNumber)))
                .OrderBy(s => s.StandNumber).Select(s => new { s.Id, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Where(s => !unavailable.Contains(s.Id)).ToList();

        var listed = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == proposal.CompanyId && a.Category == proposal.Category && a.WaitingListNumber != null && (a.Status == ApplicantStatus.Active || a.Status == ApplicantStatus.Suspended))
            .Select(a => new { a.Id, Number = a.WaitingListNumber!, a.Status, a.IdNumber, a.SpouseIdNumber }).ToListAsync(ct).ConfigureAwait(false);

        // A substitution run does not offer a stand again to whoever declined it or let its offer lapse.
        List<long> substituted = [.. stands.Select(s => s.Id)];
        HashSet<long> refused = onlyStands.Count == 0 ? [] : [.. await _db.Set<StandAllocation>().AsNoTracking()
            .Where(a => a.CompanyId == proposal.CompanyId && substituted.Contains(a.StandId) && (a.Status == AllocationStatus.Declined || a.Status == AllocationStatus.Lapsed))
            .Select(a => a.ApplicantId).ToListAsync(ct).ConfigureAwait(false)];

        // One household, N stands (brief §4.10.4; N is 1 unless the council or the scheme says otherwise): a household's stands held plus its live allocations, counted on the
        // national IDs of applicant and spouse, keep it out once it reaches the limit.
        int limit = Math.Max(1, await _db.HousingSchemes.AsNoTracking().Where(s => s.Id == proposal.SchemeId).Select(s => s.StandsPerBeneficiary).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await _db.Companies.AsNoTracking().Where(c => c.Id == proposal.CompanyId).Select(c => c.StandsPerBeneficiary).FirstOrDefaultAsync(ct).ConfigureAwait(false));
        HashSet<string> householdIds = [.. listed.SelectMany(a => new[] { a.IdNumber, a.SpouseIdNumber }).OfType<string>()];
        Dictionary<string, int> held = new(StringComparer.Ordinal);
        var holders = await _db.Customers.AsNoTracking()
            .Where(c => c.CompanyId == proposal.CompanyId && c.NationalIdNumber != null && householdIds.Contains(c.NationalIdNumber))
            .Select(c => new { Id = c.NationalIdNumber!, Count = _db.HousingStands.Count(s => s.CompanyId == proposal.CompanyId && s.CurrentHolderCustomerId == c.Id) })
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var holder in holders)
        {
            held[holder.Id] = held.GetValueOrDefault(holder.Id) + holder.Count;
        }

        var allocated = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == proposal.CompanyId && householdIds.Contains(a.IdNumber))
            .Select(a => new { a.IdNumber, Count = _db.Set<StandAllocation>().Count(x => x.ApplicantId == a.Id && Live.Contains(x.Status)) + (a.Status == ApplicantStatus.Allocated ? 1 : 0) })
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var holder in allocated)
        {
            held[holder.IdNumber] = held.GetValueOrDefault(holder.IdNumber) + holder.Count;
        }

        int Household(string idNumber, string? spouse) => held.GetValueOrDefault(idNumber) + (spouse is null || spouse == idNumber ? 0 : held.GetValueOrDefault(spouse));
        HashSet<long> spouseHolds = [.. listed.Where(a => Household(a.IdNumber, a.SpouseIdNumber) >= limit && held.GetValueOrDefault(a.IdNumber) < limit).Select(a => a.Id)];
        var applicants = listed.Where(a => a.Status == ApplicantStatus.Active && Household(a.IdNumber, a.SpouseIdNumber) < limit && !refused.Contains(a.Id)).ToList();
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
            run.Exclude(left.Id, left.Status == ApplicantStatus.Suspended ? ExclusionReason.Suspended : refused.Contains(left.Id) ? ExclusionReason.DeclinedStand
                : liveApplicants.Contains(left.Id) ? ExclusionReason.LiveAllocation : spouseHolds.Contains(left.Id) ? ExclusionReason.SpouseHolds
                : held.GetValueOrDefault(left.IdNumber) >= limit ? ExclusionReason.HoldsStand : ExclusionReason.LiveAllocation);
        }

        run.OfferDays = proposal.OfferDays < 1 ? 30 : proposal.OfferDays;
        _db.Set<AllocationRun>().Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, proposal.CompanyId, WorkflowDocumentTypes.AllocationRun, run.RunNumber, run.Allocations.Count,
            proposal.UserId, ct).ConfigureAwait(false);
        if (governed.IsFailure)
        {
            return governed.Error;
        }

        return await ViewAsync(run, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<AllocationRunView>> ApproveAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CancellationToken ct = default)
        => LoggingBlocksAsync(companyId, runNumber, approverUserId, () => _db.AtomicAsync(() => ApproveCoreAsync(companyId, runNumber, approverUserId, date, offerDays, ct), ct), ct);

    /// <inheritdoc />
    public Task<Result<AllocationRunView>> ApproveByCommitteeAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, string committeeResolution,
        CancellationToken ct = default)
        => ApproveByCommitteeAsync(companyId, runNumber, approverUserId, date, offerDays, new CommitteeMeeting(committeeResolution), ct);

    /// <inheritdoc />
    public Task<Result<AllocationRunView>> ApproveByCommitteeAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CommitteeMeeting meeting, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        return string.IsNullOrWhiteSpace(meeting.Resolution)
            ? Task.FromResult<Result<AllocationRunView>>(Error.Validation("Hse.CommitteeResolution", "Record the allocation committee's resolution."))
            : LoggingBlocksAsync(companyId, runNumber, approverUserId, () => _db.AtomicAsync(() => ApproveCoreAsync(companyId, runNumber, approverUserId, date, offerDays, ct, meeting.Resolution.Trim(),
                meeting.MeetingDate, string.IsNullOrWhiteSpace(meeting.Members) ? null : meeting.Members.Trim()), ct), ct);
    }

    /// <inheritdoc />
    public async Task<Result<AllocationRunView>> RejectLineAsync(long companyId, string runNumber, int sequence, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Hse.ReasonRequired", "The committee's rejection needs a reason.");
        }

        Result<(AllocationRun Run, StandAllocation Line)> found = await ProposedLineAsync(companyId, runNumber, sequence, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (AllocationRun run, StandAllocation line) = found.Value;
        line.Close(AllocationStatus.Cancelled, null, reason.Trim());
        run.Exclude(line.ApplicantId, ExclusionReason.CommitteeRejected, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ViewAsync(run, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<AllocationRunView>> SubstituteLineAsync(long companyId, string runNumber, int sequence, string skipReason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(skipReason))
        {
            return Error.Validation("Hse.ReasonRequired", "A substitution must state why the skipped applicant was skipped.");
        }

        Result<(AllocationRun Run, StandAllocation Line)> found = await ProposedLineAsync(companyId, runNumber, sequence, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (AllocationRun run, StandAllocation line) = found.Value;

        // The next applicant in the run's own order who is not on the run, not excluded from it, still active and without a live allocation.
        HashSet<long> taken = [.. run.Allocations.Where(a => a.Status != AllocationStatus.Cancelled).Select(a => a.ApplicantId)];
        HashSet<long> excluded = [.. run.Exclusions.Select(e => e.ApplicantId)];
        HashSet<long> live = [.. await _db.Set<StandAllocation>().AsNoTracking().Where(a => a.CompanyId == companyId && Live.Contains(a.Status)).Select(a => a.ApplicantId).ToListAsync(ct).ConfigureAwait(false)];
        List<string> order = [.. run.InputCandidates.Split('|', StringSplitOptions.RemoveEmptyEntries)];
        Dictionary<string, long> byNumber = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.WaitingListNumber != null && order.Contains(a.WaitingListNumber) && a.Status == ApplicantStatus.Active)
            .ToDictionaryAsync(a => a.WaitingListNumber!, a => a.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        long? next = order.Select(n => byNumber.TryGetValue(n, out long id) ? (long?)id : null)
            .FirstOrDefault(id => id is long candidate && !taken.Contains(candidate) && !excluded.Contains(candidate) && !live.Contains(candidate) && candidate != line.ApplicantId);
        if (next is not long substitute)
        {
            return Error.Rule("Hse.NoSubstitute", "Nobody is left on the list to substitute; reject the pairing instead and propose a substitution run later.");
        }

        run.Exclude(line.ApplicantId, ExclusionReason.CommitteeSkipped, skipReason.Trim());
        line.Substitute(substitute);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ViewAsync(run, ct).ConfigureAwait(false);
    }

    private async Task<Result<(AllocationRun Run, StandAllocation Line)>> ProposedLineAsync(long companyId, string runNumber, int sequence, CancellationToken ct)
    {
        string number = (runNumber ?? string.Empty).Trim();
        AllocationRun? run = await _db.Set<AllocationRun>().Include(r => r.Allocations).Include(r => r.Exclusions)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber == number, ct).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound("Hse.RunNotFound", $"Allocation run {number} not found.");
        }

        if (run.Status != AllocationRunStatus.Proposed)
        {
            return Error.Rule("Hse.RunNotProposed", $"Run {number} is {run.Status}; the committee decides on a proposed run.");
        }

        StandAllocation? line = run.Allocations.FirstOrDefault(a => a.Sequence == sequence);
        if (line is null || line.Status != AllocationStatus.Proposed)
        {
            return Error.NotFound("Hse.LineNotProposed", $"Run {number} has no proposed pairing {sequence}.");
        }

        return (run, line);
    }

    // A refused second allocation (a stand in the run no longer available, or blocked since) is kept as an exception entry after the approval's own
    // unit of work has rolled back, so the H39 exception report shows the attempt (brief §4.10.4).
    private async Task<Result<AllocationRunView>> LoggingBlocksAsync(long companyId, string runNumber, long approverUserId, Func<Task<Result<AllocationRunView>>> approve, CancellationToken ct)
    {
        Result<AllocationRunView> result = await approve().ConfigureAwait(false);
        if (result.IsSuccess || result.Error.Code is not ("Hse.StandUnavailable" or "Hse.StandBlocked"))
        {
            return result;
        }

        _db.ChangeTracker.Clear();
        AllocationRun? run = await _db.Set<AllocationRun>().AsNoTracking().Include(r => r.Allocations).FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber == runNumber, ct).ConfigureAwait(false);
        if (run is null)
        {
            return result;
        }

        List<long> standIds = [.. run.Allocations.Select(a => a.StandId)];
        List<HousingStand> stands = await _db.HousingStands.AsNoTracking().Where(s => standIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false);
        HashSet<long> blockedIds = [.. await _db.Set<StandBlock>().AsNoTracking().Where(b => standIds.Contains(b.StandId) && b.LiftedOn == null).Select(b => b.StandId).ToListAsync(ct).ConfigureAwait(false)];
        IEnumerable<HousingStand> offending = result.Error.Code == "Hse.StandUnavailable" ? stands.Where(s => s.Status != StandStatus.Available)
            : stands.Where(s => blockedIds.Contains(s.Id) || s.Status == StandStatus.Withheld);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (HousingStand stand in offending)
        {
            _db.AllocationBlockLogs.Add(new AllocationBlockLog(companyId, runNumber, stand.Id, stand.StandNumber, result.Error.Message, approverUserId, now));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public Task<Result> SwapAsync(long companyId, string firstCertificate, string secondCertificate, DateOnly date, string reference, string reason, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure(Error.Validation("Hse.SwapDocument", "A swap needs its document reference and the reason."));
            }

            string first = (firstCertificate ?? string.Empty).Trim(), second = (secondCertificate ?? string.Empty).Trim();
            List<StandAllocation> pair = await _db.Set<StandAllocation>().Where(a => a.CompanyId == companyId && (a.CertificateNumber == first || a.CertificateNumber == second)
                && (a.Status == AllocationStatus.Offered || a.Status == AllocationStatus.Accepted)).ToListAsync(ct).ConfigureAwait(false);
            if (first == second || pair.Count != 2)
            {
                return Result.Failure(Error.NotFound("Hse.SwapOffers", "Both certificates must be different live offers."));
            }

            List<long> standIds = [.. pair.Select(a => a.StandId)];
            List<HousingStand> stands = await _db.Set<HousingStand>().AsNoTracking().Where(s => standIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false);
            if (stands.Any(s => s.Status != StandStatus.Allocated) || stands.Select(s => s.SchemeId).Distinct().Count() != 1)
            {
                return Result.Failure(Error.Rule("Hse.SwapStands", "Only allocated, unsold stands in the same scheme can be swapped."));
            }

            // One live offer per stand: the first steps aside while the second moves onto its stand, then takes the second's.
            (StandAllocation one, StandAllocation two) = (pair[0], pair[1]);
            (long oneStand, decimal? onePrice, AllocationStatus oneStatus) = (one.StandId, one.OfferedPrice, one.Status);
            one.HoldForSwap();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            (long twoStand, decimal? twoPrice) = (two.StandId, two.OfferedPrice);
            two.TakeStand(oneStand, onePrice, two.Status);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            one.TakeStand(twoStand, twoPrice, oneStatus);
            _db.Set<StandSwap>().Add(new StandSwap(companyId, first, second, date, reference.Trim(), reason.Trim(), userId));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }, ct);

    private async Task<Result<AllocationRunView>> ApproveCoreAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CancellationToken ct,
        string? committeeResolution = null, DateOnly? meetingDate = null, string? members = null)
    {
        string reference = (runNumber ?? string.Empty).Trim();
        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.AllocationRun, reference, ct).ConfigureAwait(false))
        {
            return Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.AllocationRun, reference);
        }

        return await ConcludeApprovalAsync(companyId, reference, approverUserId, date, offerDays, ct, committeeResolution, meetingDate, members).ConfigureAwait(false);
    }

    /// <summary>Approves a proposed run: offers go out (the workflow's approval lands here too).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="date">The approval date.</param>
    /// <param name="offerDays">How long offers stay open.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run, or why it cannot be approved.</returns>
    internal async Task<Result<AllocationRunView>> ConcludeApprovalAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CancellationToken ct,
        string? committeeResolution = null, DateOnly? meetingDate = null, string? committeeMembers = null)
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

        // Pairings the committee rejected stay out; their stands stay available.
        List<long> standIds = [.. run.Allocations.Where(a => a.Status == AllocationStatus.Proposed).Select(a => a.StandId)];
        if (standIds.Count == 0)
        {
            return Error.Rule("Hse.NothingToApprove", $"Every pairing on run {runNumber} was rejected; cancel it and propose again.");
        }

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

        if (meetingDate is not null || committeeMembers is not null)
        {
            run.MeetingDate = meetingDate;
            run.CommitteeMembers = committeeMembers;
        }

        foreach (StandAllocation allocation in run.Allocations.Where(a => a.Status == AllocationStatus.Proposed).OrderBy(a => a.Sequence))
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
        run.CommitteeResolution = committeeResolution;
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

/// <summary>Concludes an allocation run's approval workflow: approval sends the offers, anything else cancels the run.</summary>
internal sealed class AllocationRunWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;
    private readonly AllocationService _service;

    public AllocationRunWorkflowHandler(One4AllDbContext db, AllocationService service)
    {
        _db = db;
        _service = service;
    }

    public string DocumentType => WorkflowDocumentTypes.AllocationRun;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        AllocationRun? run = await _db.Set<AllocationRun>().AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber == documentReference && r.Status == AllocationRunStatus.Proposed, ct)
            .ConfigureAwait(false);
        if (run is null)
        {
            return Result.Success();
        }

        if (outcome != Core.Workflow.WorkflowStatus.Approved)
        {
            return await _service.CancelAsync(companyId, documentReference, ct).ConfigureAwait(false);
        }

        Result<AllocationRunView> approved = await _service.ConcludeApprovalAsync(companyId, documentReference, decidedByUserId, DateOnly.FromDateTime(DateTime.Today), run.OfferDays, ct,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()).ConfigureAwait(false);
        return approved.IsFailure ? Result.Failure(approved.Error) : Result.Success();
    }
}
