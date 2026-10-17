using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A request to propose an allocation run.</summary>
public sealed class AllocationProposal
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The scheme whose available stands are allocated.</summary>
    public long SchemeId { get; init; }

    /// <summary>The waiting-list category drawn from.</summary>
    public HousingCategory Category { get; init; }

    /// <summary>How the list is ordered.</summary>
    public AllocationMethod Method { get; init; }

    /// <summary>The ballot seed; a ballot without one draws a random seed, which is recorded.</summary>
    public long? Seed { get; init; }

    /// <summary>The date scores and ranks are worked out at.</summary>
    public DateOnly AsAtDate { get; init; }

    /// <summary>Who proposes it.</summary>
    public long UserId { get; init; }

    /// <summary>How long offers stay open once the run is approved (used when an approval workflow approves it).</summary>
    public int OfferDays { get; init; } = 30;

    /// <summary>Only these stands (a substitution run for stands declined or lapsed), or empty for every free stand in the scheme.</summary>
    public IReadOnlyList<string> StandNumbers { get; init; } = [];
}

/// <summary>A pairing on an allocation list.</summary>
/// <param name="Sequence">Its place on the list.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="WaitingListNumber">The applicant.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="CertificateNumber">The allocation certificate, once approved.</param>
/// <param name="OfferedPrice">The stand's price frozen at the offer date, when an approved price applied.</param>
public sealed record AllocationLine(int Sequence, string StandNumber, string WaitingListNumber, AllocationStatus Status, string? CertificateNumber, decimal? OfferedPrice = null);

/// <summary>An allocation run and its list.</summary>
/// <param name="RunNumber">The run number.</param>
/// <param name="Method">How the list was ordered.</param>
/// <param name="Seed">The ballot seed, for a ballot.</param>
/// <param name="InputHash">The hash of the inputs.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Lines">The pairings.</param>
/// <summary>The allocation committee's meeting (brief §4.10.4).</summary>
/// <param name="Resolution">The minute or resolution reference.</param>
/// <param name="MeetingDate">When the committee met.</param>
/// <param name="Members">The members present.</param>
public sealed record CommitteeMeeting(string Resolution, DateOnly? MeetingDate = null, string? Members = null);

public sealed record AllocationRunView(string RunNumber, AllocationMethod Method, long? Seed, string InputHash, AllocationRunStatus Status, IReadOnlyList<AllocationLine> Lines);

/// <summary>The result of re-verifying a draw.</summary>
/// <param name="HashMatches">Whether the stored inputs still hash to the stored hash.</param>
/// <param name="OrderMatches">Whether re-running the draw from the stored seed and inputs gives the stored list.</param>
public sealed record DrawVerification(bool HashMatches, bool OrderMatches)
{
    /// <summary>Whether the draw is verified.</summary>
    public bool IsVerified => HashMatches && OrderMatches;
}

/// <summary>
/// Stand allocation (brief §4.10.4): a proposed list from a scheme's available stands and a waiting-list category by
/// seniority, score or a seeded, re-verifiable ballot; approval by someone other than the proposer issuing certificates
/// and offers with a deadline; acceptance, decline and lapse of offers; and verification of a draw. A stand and an
/// applicant each have at most one live allocation, enforced in the database.
/// </summary>
public interface IAllocationService
{
    /// <summary>Proposes an allocation run.</summary>
    /// <param name="proposal">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The proposed list, or why there is none.</returns>
    Task<Result<AllocationRunView>> ProposeAsync(AllocationProposal proposal, CancellationToken ct = default);

    /// <summary>Approves a proposed run: certificates are issued and offers made, stands marked allocated. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="approverUserId">The approver (not the proposer).</param>
    /// <param name="date">The approval date.</param>
    /// <param name="offerDays">Days the offers stay open.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The approved list, or why it was refused.</returns>
    Task<Result<AllocationRunView>> ApproveAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CancellationToken ct = default);

    /// <summary>Approves a proposed run on the allocation committee's resolution, which the run keeps (brief §4.10.2).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="approverUserId">The approver (not the proposer).</param>
    /// <param name="date">The approval date.</param>
    /// <param name="offerDays">Days the offers stay open.</param>
    /// <param name="committeeResolution">The committee's resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The approved list, or why it was refused.</returns>
    Task<Result<AllocationRunView>> ApproveByCommitteeAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, string committeeResolution, CancellationToken ct = default);

    /// <summary>Approves a proposed run on the committee's decision, recording the meeting (minute reference, date, members present) on the run.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="approverUserId">The approver (not the proposer).</param>
    /// <param name="date">The approval date.</param>
    /// <param name="offerDays">Days the offers stay open.</param>
    /// <param name="meeting">The meeting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The approved list, or why it was refused.</returns>
    Task<Result<AllocationRunView>> ApproveByCommitteeAsync(long companyId, string runNumber, long approverUserId, DateOnly date, int offerDays, CommitteeMeeting meeting, CancellationToken ct = default);

    /// <summary>
    /// The committee rejects one proposed pairing with a reason (brief §4.10.4): the applicant is recorded as rejected on the run, the stand stays
    /// available (it can go in a substitution run), and the rest of the run goes on to approval.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run (still proposed).</param>
    /// <param name="sequence">The pairing's sequence.</param>
    /// <param name="reason">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run as it now stands, or why not.</returns>
    Task<Result<AllocationRunView>> RejectLineAsync(long companyId, string runNumber, int sequence, string reason, CancellationToken ct = default);

    /// <summary>
    /// The committee substitutes the next eligible applicant on the list for the one on a proposed pairing, stating why the skipped applicant was
    /// skipped (kept on the run as an exclusion).
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run (still proposed).</param>
    /// <param name="sequence">The pairing's sequence.</param>
    /// <param name="skipReason">Why the applicant is skipped.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run as it now stands, or why not (<c>Hse.NoSubstitute</c> when nobody is left on the list).</returns>
    Task<Result<AllocationRunView>> SubstituteLineAsync(long companyId, string runNumber, int sequence, string skipReason, CancellationToken ct = default);

    /// <summary>
    /// Swaps the stands of two allottees whose offers are live and whose stands are not yet sold, in the same scheme (brief §4.10.2): the
    /// stands and their frozen prices change hands, recorded in a swap document.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="firstCertificate">One allottee's certificate.</param>
    /// <param name="secondCertificate">The other's.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The swap document.</param>
    /// <param name="reason">Why.</param>
    /// <param name="userId">Who approved it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why the two cannot swap.</returns>
    Task<Result> SwapAsync(long companyId, string firstCertificate, string secondCertificate, DateOnly date, string reference, string reason, long userId, CancellationToken ct = default);

    /// <summary>Cancels a proposed run, freeing its stands and applicants.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default);

    /// <summary>Records an applicant's acceptance of an open offer.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="certificateNumber">The allocation certificate.</param>
    /// <param name="date">The acceptance date (by the deadline).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AcceptAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Records an applicant's decline: the stand returns to the pool and the applicant stays on the list.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="certificateNumber">The allocation certificate.</param>
    /// <param name="date">The decline date.</param>
    /// <param name="reason">Why (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> DeclineAsync(long companyId, string certificateNumber, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>Lapses every open offer past its deadline, returning the stands to the pool.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificates lapsed.</returns>
    Task<IReadOnlyList<string>> LapseExpiredAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Re-verifies a run's draw from its stored seed and inputs.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verification, or why there is none.</returns>
    Task<Result<DrawVerification>> VerifyDrawAsync(long companyId, string runNumber, CancellationToken ct = default);
}
