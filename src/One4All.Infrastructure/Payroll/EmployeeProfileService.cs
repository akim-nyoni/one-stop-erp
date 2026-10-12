using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Keeps the rest of the employee master: contacts, post details, the photograph, beneficiaries and medical-aid dependants.</summary>
public sealed class EmployeeProfileService : IEmployeeProfileService
{
    private const int MaxPhotoBytes = 2 * 1024 * 1024;
    private static readonly string[] PhotoTypes = ["image/jpeg", "image/png", "image/bmp"];

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public EmployeeProfileService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetDetailsAsync(long companyId, string employeeNumber, EmployeeProfileUpdate update, CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return Result.Failure(NoEmployee(employeeNumber));
        }

        if (update.NoticePeriodDays is < 0 or > 365)
        {
            return Result.Failure(Error.Validation("Hr.NoticePeriod", "The notice period is between 0 and 365 days."));
        }

        static string? Set(string? value, string? current) => value is null ? current : string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        employee.PhysicalAddress = Set(update.PhysicalAddress, employee.PhysicalAddress);
        employee.PostalAddress = Set(update.PostalAddress, employee.PostalAddress);
        employee.Phone = Set(update.Phone, employee.Phone);
        employee.JobTitle = Set(update.JobTitle, employee.JobTitle);
        employee.WorkersCompensationClass = Set(update.WorkersCompensationClass, employee.WorkersCompensationClass)?.ToUpperInvariant();
        employee.NoticePeriodDays = update.NoticePeriodDays ?? employee.NoticePeriodDays;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetPhotoAsync(long companyId, string employeeNumber, byte[]? photo, string? contentType, CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return Result.Failure(NoEmployee(employeeNumber));
        }

        string? type = contentType?.Trim().ToLowerInvariant();
        if (photo is not null && (photo.Length == 0 || photo.Length > MaxPhotoBytes || type is null || !PhotoTypes.Contains(type)))
        {
            return Result.Failure(Error.Validation("Hr.Photo", "A photograph is a JPEG, PNG or BMP image of at most 2 MB."));
        }

        employee.Photo = photo;
        employee.PhotoContentType = photo is null ? null : type;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddDependantAsync(long companyId, string employeeNumber, NewDependant dependant, CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return NoEmployee(employeeNumber);
        }

        if (string.IsNullOrWhiteSpace(dependant.Name) || string.IsNullOrWhiteSpace(dependant.Relationship))
        {
            return Error.Validation("Hr.DependantRequired", "A dependant or beneficiary needs a name and the relationship to the employee.");
        }

        bool beneficiary = dependant.Kind is DependantKind.Beneficiary or DependantKind.Both;
        if (beneficiary && dependant.SharePercent is not (> 0m and <= 100m))
        {
            return Error.Validation("Hr.BeneficiaryShare", "A beneficiary needs a share between 0 and 100%.");
        }

        if (beneficiary)
        {
            decimal allotted = await _db.EmployeeDependants.Where(d => d.CompanyId == companyId && d.EmployeeId == employee.Id
                    && (d.Kind == DependantKind.Beneficiary || d.Kind == DependantKind.Both))
                .SumAsync(d => d.SharePercent ?? 0m, ct).ConfigureAwait(false);
            if (allotted + dependant.SharePercent!.Value > 100m)
            {
                return Error.Conflict("Hr.BeneficiaryShares", $"The beneficiaries already share {allotted:0.##}%; only {100m - allotted:0.##}% is left.");
            }
        }

        EmployeeDependant added = new(companyId, employee.Id, dependant.Kind, dependant.Name.Trim(), dependant.Relationship.Trim())
        {
            DateOfBirth = dependant.DateOfBirth, IdNumber = Clean(dependant.IdNumber), SharePercent = beneficiary ? dependant.SharePercent : null,
            MedicalAidNumber = dependant.Kind == DependantKind.Beneficiary ? null : Clean(dependant.MedicalAidNumber),
        };
        _db.EmployeeDependants.Add(added);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return added.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveDependantAsync(long companyId, long dependantId, CancellationToken ct = default)
    {
        if (await _db.EmployeeDependants.FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Id == dependantId, ct).ConfigureAwait(false) is not EmployeeDependant dependant)
        {
            return Result.Failure(Error.NotFound("Hr.DependantNotFound", $"Dependant {dependantId} not found."));
        }

        _db.EmployeeDependants.Remove(dependant);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmployeeDependant>> DependantsAsync(long companyId, string employeeNumber, CancellationToken ct = default)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        return await _db.EmployeeDependants.AsNoTracking()
            .Where(d => d.CompanyId == companyId && _db.Employees.Any(e => e.Id == d.EmployeeId && e.EmployeeNumber == number))
            .OrderBy(d => d.Kind).ThenBy(d => d.Name).ToListAsync(ct).ConfigureAwait(false);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error NoEmployee(string number) => Error.NotFound("Hr.EmployeeNotFound", $"Employee {number} not found.");

    private Task<Employee?> EmployeeAsync(long companyId, string employeeNumber, CancellationToken ct)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        return _db.Employees.FirstOrDefaultAsync(e => e.CompanyId == companyId && e.EmployeeNumber == number, ct);
    }
}
