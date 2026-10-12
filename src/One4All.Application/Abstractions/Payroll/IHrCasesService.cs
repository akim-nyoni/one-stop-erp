using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>
/// Grievances, company property issued to staff and restricted medical records (brief §4.15.6). Medical records need the
/// <see cref="MedicalPermission"/> permission to read (View) or record (Edit); everyone else is refused.
/// </summary>
public interface IHrCasesService
{
    /// <summary>The permission that guards medical records.</summary>
    const string MedicalPermission = "Hr.Medical";

    /// <summary>Lodges a grievance.</summary>
    Task<Result<long>> LodgeGrievanceAsync(long companyId, string employeeNumber, DateOnly lodgedOn, string subject, string details, string? against, CancellationToken ct = default);

    /// <summary>Moves a grievance on: investigating, resolved or withdrawn (the last two with the outcome).</summary>
    Task<Result> MoveGrievanceAsync(long companyId, long grievanceId, GrievanceStatus status, DateOnly on, string? outcome, CancellationToken ct = default);

    /// <summary>Issues company property to an employee.</summary>
    Task<Result<long>> IssueAssetAsync(long companyId, string employeeNumber, string description, string? serialNumber, decimal value, DateOnly issuedOn, CancellationToken ct = default);

    /// <summary>Records the return of issued property.</summary>
    Task<Result> ReturnAssetAsync(long companyId, long issueId, DateOnly returnedOn, string? condition, CancellationToken ct = default);

    /// <summary>What an employee still has out.</summary>
    Task<IReadOnlyList<EmployeeAssetIssue>> OutstandingAssetsAsync(long companyId, long employeeId, CancellationToken ct = default);

    /// <summary>Records a medical note (needs the medical permission with Edit).</summary>
    Task<Result<long>> RecordMedicalAsync(long companyId, string employeeNumber, DateOnly date, MedicalRecordKind kind, string note, bool? fitForWork, DateOnly? reviewOn, long userId,
        CancellationToken ct = default);

    /// <summary>An employee's medical records (needs the medical permission).</summary>
    Task<Result<IReadOnlyList<EmployeeMedicalRecord>>> MedicalRecordsAsync(long companyId, string employeeNumber, long userId, CancellationToken ct = default);
}
