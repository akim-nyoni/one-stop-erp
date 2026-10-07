using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.Schools;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>Bursary approval, the staff-child discount and do-not-disclose (brief §4.11).</summary>
public sealed class SchoolBursaryService : ISchoolBursaryService
{
    private readonly One4AllDbContext _db;
    private readonly IPermissionService? _permissions;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="permissions">Permission checks (the bursary approval right).</param>
    /// <param name="workflow">The approval workflow.</param>
    public SchoolBursaryService(One4AllDbContext db, IPermissionService? permissions = null, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _permissions = permissions;
        _workflow = workflow;
    }

    /// <inheritdoc />
    public Task<Result<BursaryRequestResult>> RequestAsync(long companyId, long studentId, BursaryType type, decimal value, string reason, long userId, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (type == BursaryType.None || value <= 0m || (type == BursaryType.Percentage && value > 100m) || string.IsNullOrWhiteSpace(reason))
            {
                return Error.Validation("School.BursaryInvalid", "A bursary needs a percentage (up to 100) or a positive value, and a reason.");
            }

            Student? student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false);
            if (student is null)
            {
                return Error.NotFound("School.StudentNotFound", $"Student {studentId} not found.");
            }

            if (await _db.Set<BursaryAward>().AnyAsync(a => a.StudentId == studentId && a.Status == BursaryAwardStatus.Pending, ct).ConfigureAwait(false))
            {
                return Error.Conflict("School.BursaryPending", $"{student.AdmissionNumber} already has a bursary request awaiting approval.");
            }

            int count = await _db.Set<BursaryAward>().CountAsync(a => a.StudentId == studentId, ct).ConfigureAwait(false);
            string reference = $"BUR-{student.AdmissionNumber}-{count + 1}";
            _db.Set<BursaryAward>().Add(new BursaryAward(companyId, studentId, reference, type, value, reason.Trim(), userId, date));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.Bursary, reference, value, userId, ct).ConfigureAwait(false);
            return governed.IsFailure ? governed.Error : Result<BursaryRequestResult>.Success(new BursaryRequestResult(reference, governed.Value));
        }, ct);

    /// <inheritdoc />
    public Task<Result> DecideAsync(long companyId, string reference, bool approve, long userId, DateOnly date, string? note, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            string key = (reference ?? string.Empty).Trim().ToUpperInvariant();
            if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.Bursary, key, ct).ConfigureAwait(false))
            {
                return Result.Failure(Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.Bursary, key));
            }

            BursaryAward? award = await _db.Set<BursaryAward>().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Reference == key && a.Status == BursaryAwardStatus.Pending, ct).ConfigureAwait(false);
            if (award is null)
            {
                return Result.Failure(Error.NotFound("School.BursaryNotPending", $"No bursary request {key} is awaiting approval."));
            }

            if (award.RequestedByUserId == userId)
            {
                return Result.Failure(Error.Forbidden("School.BursarySelfApproval", "A bursary cannot be decided by the person who asked for it."));
            }

            if (_permissions is null || !(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has("School.Bursary", OperationFlags.Approve))
            {
                return Result.Failure(Error.Forbidden("School.BursaryApprover", "Deciding a bursary needs the bursary approval right."));
            }

            return await ConcludeAsync(award, approve, userId, date, note, ct).ConfigureAwait(false);
        }, ct);

    /// <summary>Records the decision; approval applies the bursary to the student.</summary>
    internal async Task<Result> ConcludeAsync(BursaryAward award, bool approve, long userId, DateOnly date, string? note, CancellationToken ct)
    {
        award.Decide(approve, userId, date, note);
        if (approve)
        {
            Student student = await _db.Students.FirstAsync(s => s.Id == award.StudentId, ct).ConfigureAwait(false);
            student.GrantBursary(award.Type, award.Value);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetStaffChildDiscountAsync(long companyId, decimal percent, CancellationToken ct = default)
    {
        if (percent is < 0m or > 100m)
        {
            return Result.Failure(Error.Validation("School.StaffDiscount", "The staff-child discount is between 0 and 100%."));
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        company.StaffChildDiscountPercent = percent;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetStaffParentAsync(long companyId, long studentId, long? employeeId, CancellationToken ct = default)
    {
        Student? student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (student is null)
        {
            return Result.Failure(Error.NotFound("School.StudentNotFound", $"Student {studentId} not found."));
        }

        if (employeeId is long id && !await _db.Employees.AnyAsync(e => e.Id == id && e.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Payroll.EmployeeNotFound", $"Employee {id} not found."));
        }

        student.StaffEmployeeId = employeeId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetDisclosureAsync(long companyId, long studentId, bool doNotDisclose, string? note, CancellationToken ct = default)
    {
        Student? student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (student is null)
        {
            return Result.Failure(Error.NotFound("School.StudentNotFound", $"Student {studentId} not found."));
        }

        if (doNotDisclose && string.IsNullOrWhiteSpace(note))
        {
            return Result.Failure(Error.Validation("School.DisclosureNote", "Say why the student's details are withheld (court order, protection…)."));
        }

        student.SetDisclosure(doNotDisclose, note);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Concludes a bursary's approval workflow: approval applies it, anything else rejects it.</summary>
internal sealed class BursaryWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;
    private readonly SchoolBursaryService _service;

    public BursaryWorkflowHandler(One4AllDbContext db, SchoolBursaryService service)
    {
        _db = db;
        _service = service;
    }

    public string DocumentType => WorkflowDocumentTypes.Bursary;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        BursaryAward? award = await _db.Set<BursaryAward>().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Reference == documentReference && a.Status == BursaryAwardStatus.Pending, ct)
            .ConfigureAwait(false);
        return award is null ? Result.Success()
            : await _service.ConcludeAsync(award, outcome == Core.Workflow.WorkflowStatus.Approved, decidedByUserId, DateOnly.FromDateTime(DateTime.Today), comment, ct).ConfigureAwait(false);
    }
}
