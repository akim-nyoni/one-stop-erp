using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Keeps grievances, property issued to staff and restricted medical records.</summary>
public sealed class HrCasesService : IHrCasesService
{
    private readonly One4AllDbContext _db;
    private readonly IPermissionService _permissions;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="permissions">Checks the medical-records permission.</param>
    public HrCasesService(One4AllDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result<long>> LodgeGrievanceAsync(long companyId, string employeeNumber, DateOnly lodgedOn, string subject, string details, string? against, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(details))
        {
            return Error.Validation("Hr.Grievance", "A grievance needs a subject and the employee's account of it.");
        }

        if (await EmployeeIdAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not long employeeId)
        {
            return NoEmployee(employeeNumber);
        }

        EmployeeGrievance grievance = new(companyId, employeeId, lodgedOn, subject.Trim(), details.Trim()) { Against = Clean(against) };
        _db.Set<EmployeeGrievance>().Add(grievance);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return grievance.Id;
    }

    /// <inheritdoc />
    public async Task<Result> MoveGrievanceAsync(long companyId, long grievanceId, GrievanceStatus status, DateOnly on, string? outcome, CancellationToken ct = default)
    {
        if (await _db.Set<EmployeeGrievance>().FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == grievanceId, ct).ConfigureAwait(false) is not EmployeeGrievance grievance)
        {
            return Result.Failure(Error.NotFound("Hr.GrievanceNotFound", $"Grievance {grievanceId} not found."));
        }

        if (status is GrievanceStatus.Resolved or GrievanceStatus.Withdrawn && string.IsNullOrWhiteSpace(outcome))
        {
            return Result.Failure(Error.Validation("Hr.GrievanceOutcome", "Closing a grievance needs its outcome."));
        }

        if (!grievance.Move(status, on, Clean(outcome)))
        {
            return Result.Failure(Error.Rule("Hr.GrievanceStage", $"A grievance that is {grievance.Status} cannot become {status}."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> IssueAssetAsync(long companyId, string employeeNumber, string description, string? serialNumber, decimal value, DateOnly issuedOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description) || value < 0m)
        {
            return Error.Validation("Hr.AssetIssue", "Issued property needs a description (and a value that is not negative).");
        }

        if (await EmployeeIdAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not long employeeId)
        {
            return NoEmployee(employeeNumber);
        }

        EmployeeAssetIssue issue = new(companyId, employeeId, description.Trim(), issuedOn) { SerialNumber = Clean(serialNumber), Value = value };
        _db.Set<EmployeeAssetIssue>().Add(issue);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return issue.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ReturnAssetAsync(long companyId, long issueId, DateOnly returnedOn, string? condition, CancellationToken ct = default)
    {
        if (await _db.Set<EmployeeAssetIssue>().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Id == issueId, ct).ConfigureAwait(false) is not EmployeeAssetIssue issue)
        {
            return Result.Failure(Error.NotFound("Hr.AssetIssueNotFound", $"Issue {issueId} not found."));
        }

        if (issue.ReturnedOn is not null || returnedOn < issue.IssuedOn)
        {
            return Result.Failure(Error.Rule("Hr.AssetReturned", $"{issue.Description} is already back, or the return date is before it was issued."));
        }

        issue.Return(returnedOn, Clean(condition));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmployeeAssetIssue>> OutstandingAssetsAsync(long companyId, long employeeId, CancellationToken ct = default)
        => await _db.Set<EmployeeAssetIssue>().AsNoTracking().Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId && a.ReturnedOn == null)
            .OrderBy(a => a.IssuedOn).ThenBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<long>> RecordMedicalAsync(long companyId, string employeeNumber, DateOnly date, MedicalRecordKind kind, string note, bool? fitForWork, DateOnly? reviewOn, long userId,
        CancellationToken ct = default)
    {
        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(IHrCasesService.MedicalPermission, OperationFlags.Edit))
        {
            return Restricted();
        }

        if (string.IsNullOrWhiteSpace(note) || reviewOn < date)
        {
            return Error.Validation("Hr.Medical", "A medical record needs a note (and a review date after it, if any).");
        }

        if (await EmployeeIdAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not long employeeId)
        {
            return NoEmployee(employeeNumber);
        }

        EmployeeMedicalRecord record = new(companyId, employeeId, date, kind, note.Trim()) { FitForWork = fitForWork, ReviewOn = reviewOn };
        _db.Set<EmployeeMedicalRecord>().Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EmployeeMedicalRecord>>> MedicalRecordsAsync(long companyId, string employeeNumber, long userId, CancellationToken ct = default)
    {
        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(IHrCasesService.MedicalPermission, OperationFlags.View))
        {
            return Restricted();
        }

        if (await EmployeeIdAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not long employeeId)
        {
            return NoEmployee(employeeNumber);
        }

        return await _db.Set<EmployeeMedicalRecord>().AsNoTracking().Where(m => m.CompanyId == companyId && m.EmployeeId == employeeId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
    }

    private static Error Restricted() => Error.Forbidden("Hr.MedicalRestricted", "Medical records are restricted to users with the medical-records permission.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error NoEmployee(string number) => Error.NotFound("Hr.EmployeeNotFound", $"Employee {number} not found.");

    private Task<long?> EmployeeIdAsync(long companyId, string employeeNumber, CancellationToken ct)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        return _db.Employees.Where(e => e.CompanyId == companyId && e.EmployeeNumber == number).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct);
    }
}
