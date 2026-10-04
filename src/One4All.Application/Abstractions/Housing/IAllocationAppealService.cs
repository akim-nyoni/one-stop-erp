using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An appeal on the register.</summary>
/// <param name="AppealNumber">The appeal.</param>
/// <param name="WaitingListNumber">The appellant.</param>
/// <param name="RunNumber">The run appealed against.</param>
/// <param name="LodgedOn">When it was lodged.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="HearingDate">The hearing date.</param>
/// <param name="ReallocationCertificate">The resulting re-allocation, if any.</param>
public sealed record AppealEntry(string AppealNumber, string WaitingListNumber, string RunNumber, DateOnly LodgedOn, AppealStatus Status, DateOnly? HearingDate, string? ReallocationCertificate);

/// <summary>
/// The allocation appeals register (brief §4.10.4): unsuccessful applicants appeal against an approved allocation run on
/// stated grounds; a hearing is set; the appeal is upheld or dismissed with reasons; an upheld appeal can be linked to
/// the allocation certificate of the re-allocation it led to.
/// </summary>
public interface IAllocationAppealService
{
    /// <summary>Lodges an appeal by a waiting-list applicant who was not allocated in the run.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The appellant.</param>
    /// <param name="runNumber">The approved run appealed against.</param>
    /// <param name="grounds">The grounds (required).</param>
    /// <param name="lodgedOn">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The appeal number, or why it was refused.</returns>
    Task<Result<string>> LodgeAsync(long companyId, string waitingListNumber, string runNumber, string grounds, DateOnly lodgedOn, CancellationToken ct = default);

    /// <summary>Sets the hearing date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="appealNumber">The appeal.</param>
    /// <param name="hearingDate">The hearing date (not before lodging).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetHearingAsync(long companyId, string appealNumber, DateOnly hearingDate, CancellationToken ct = default);

    /// <summary>Records the outcome after the hearing.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="appealNumber">The appeal.</param>
    /// <param name="upheld">Upheld or dismissed.</param>
    /// <param name="date">The decision date (on or after the hearing).</param>
    /// <param name="reasons">Why (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> DecideAsync(long companyId, string appealNumber, bool upheld, DateOnly date, string reasons, CancellationToken ct = default);

    /// <summary>Links an upheld appeal to the allocation certificate issued to the appellant as a result.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="appealNumber">The appeal.</param>
    /// <param name="certificateNumber">The certificate (issued to the appellant).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> LinkReallocationAsync(long companyId, string appealNumber, string certificateNumber, CancellationToken ct = default);

    /// <summary>The appeals register, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The appeals.</returns>
    Task<IReadOnlyList<AppealEntry>> RegisterAsync(long companyId, CancellationToken ct = default);
}
