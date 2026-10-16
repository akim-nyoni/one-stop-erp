using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A second allocation of a stand that was refused (brief §4.10.4): an allocation run's approval found a stand no longer available or blocked.
/// The refusal itself is enforced by the stand's status and the unique claims; this entry is the exception-report record of the attempt.
/// </summary>
public sealed class AllocationBlockLog : Entity, ICompanyScoped
{
    private AllocationBlockLog()
    {
        RunNumber = string.Empty;
        StandNumber = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the entry.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The allocation run.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="standNumber">The stand number.</param>
    /// <param name="reason">Why it was refused (the error code's message).</param>
    /// <param name="attemptedByUserId">Who tried to approve.</param>
    /// <param name="attemptedOnUtc">When.</param>
    public AllocationBlockLog(long companyId, string runNumber, long standId, string standNumber, string reason, long attemptedByUserId, DateTimeOffset attemptedOnUtc)
    {
        CompanyId = companyId;
        RunNumber = runNumber;
        StandId = standId;
        StandNumber = standNumber;
        Reason = reason;
        AttemptedByUserId = attemptedByUserId;
        AttemptedOnUtc = attemptedOnUtc;
    }

    /// <summary>Gets the council.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the allocation run whose approval was refused.</summary>
    public string RunNumber { get; private set; }

    /// <summary>Gets the stand.</summary>
    public long StandId { get; private set; }

    /// <summary>Gets the stand number.</summary>
    public string StandNumber { get; private set; }

    /// <summary>Gets why the allocation was refused.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets who tried to approve the run.</summary>
    public long AttemptedByUserId { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset AttemptedOnUtc { get; private set; }
}
