using One4All.Core.Common;
using One4All.Core.Schools;

namespace One4All.Application.Abstractions.Schools;

/// <summary>A bursary request made.</summary>
/// <param name="Reference">Its reference.</param>
/// <param name="InWorkflow">Whether the approval workflow took it (otherwise it waits for an approver's decision).</param>
public sealed record BursaryRequestResult(string Reference, bool InWorkflow);

/// <summary>
/// School fee controls (brief §4.11): bursaries reduce fees only once approved by someone other than the requester, children of staff take the staff
/// discount while the parent is employed, and a student can be marked do-not-disclose.
/// </summary>
public interface ISchoolBursaryService
{
    /// <summary>Asks for a bursary; it waits for approval (or goes into the approval workflow when one is defined).</summary>
    /// <remarks>A bursary may be given an expiry (the last day it applies); fees billed after it get no bursary.</remarks>
    Task<Result<BursaryRequestResult>> RequestAsync(long companyId, long studentId, BursaryType type, decimal value, string reason, long userId, DateOnly date, DateOnly? validUntil = null,
        CancellationToken ct = default);

    /// <summary>The students whose bursaries expire within the given days of a date (for renewal), soonest first.</summary>
    Task<IReadOnlyList<ExpiringBursary>> ExpiringAsync(long companyId, DateOnly asAt, int withinDays, CancellationToken ct = default);

    /// <summary>Approves or rejects a pending bursary: never by its requester, and only with the bursary approval right; approval applies it to the student.</summary>
    Task<Result> DecideAsync(long companyId, string reference, bool approve, long userId, DateOnly date, string? note, CancellationToken ct = default);

    /// <summary>Sets the staff-child discount percentage (0–100).</summary>
    Task<Result> SetStaffChildDiscountAsync(long companyId, decimal percent, CancellationToken ct = default);

    /// <summary>Links a student to the staff member who is their parent (null removes it).</summary>
    Task<Result> SetStaffParentAsync(long companyId, long studentId, long? employeeId, CancellationToken ct = default);

    /// <summary>Sets or lifts do-not-disclose on a student.</summary>
    Task<Result> SetDisclosureAsync(long companyId, long studentId, bool doNotDisclose, string? note, CancellationToken ct = default);
}

/// <summary>A bursary coming up for renewal.</summary>
/// <param name="AdmissionNumber">The student.</param>
/// <param name="StudentName">The student's name (withheld for restricted records).</param>
/// <param name="Type">Percentage or value.</param>
/// <param name="Value">The percentage or value.</param>
/// <param name="ValidUntil">The last day it applies.</param>
public sealed record ExpiringBursary(string AdmissionNumber, string StudentName, BursaryType Type, decimal Value, DateOnly ValidUntil);
