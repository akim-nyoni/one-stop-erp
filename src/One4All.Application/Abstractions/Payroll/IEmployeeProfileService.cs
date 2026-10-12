using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>
/// The rest of the employee master (brief §4.15.1): addresses and phone, job title, notice period, workers' compensation class,
/// the photograph, and the beneficiaries and medical-aid dependants.
/// </summary>
public interface IEmployeeProfileService
{
    /// <summary>Sets the contact and post details (blank fields stay as they are; a single space clears one).</summary>
    Task<Result> SetDetailsAsync(long companyId, string employeeNumber, EmployeeProfileUpdate update, CancellationToken ct = default);

    /// <summary>Sets (or, with null, removes) the employee's photograph (JPEG, PNG or BMP; at most 2 MB).</summary>
    Task<Result> SetPhotoAsync(long companyId, string employeeNumber, byte[]? photo, string? contentType, CancellationToken ct = default);

    /// <summary>Adds a beneficiary or medical-aid dependant (beneficiaries' shares may not add up to more than 100%).</summary>
    /// <returns>The dependant's id.</returns>
    Task<Result<long>> AddDependantAsync(long companyId, string employeeNumber, NewDependant dependant, CancellationToken ct = default);

    /// <summary>Removes a dependant or beneficiary.</summary>
    Task<Result> RemoveDependantAsync(long companyId, long dependantId, CancellationToken ct = default);

    /// <summary>Lists an employee's dependants and beneficiaries.</summary>
    Task<IReadOnlyList<EmployeeDependant>> DependantsAsync(long companyId, string employeeNumber, CancellationToken ct = default);
}

/// <summary>Contact and post details to change (null = unchanged).</summary>
public sealed record EmployeeProfileUpdate
{
    /// <summary>The residential address.</summary>
    public string? PhysicalAddress { get; init; }

    /// <summary>The postal address.</summary>
    public string? PostalAddress { get; init; }

    /// <summary>The employee's phone.</summary>
    public string? Phone { get; init; }

    /// <summary>The job title / position.</summary>
    public string? JobTitle { get; init; }

    /// <summary>The notice period in days.</summary>
    public int? NoticePeriodDays { get; init; }

    /// <summary>The workers' compensation class.</summary>
    public string? WorkersCompensationClass { get; init; }
}

/// <summary>A beneficiary or medical-aid dependant to add.</summary>
/// <param name="Kind">Beneficiary, medical-aid dependant or both.</param>
/// <param name="Name">The person's name.</param>
/// <param name="Relationship">The relationship to the employee.</param>
public sealed record NewDependant(DependantKind Kind, string Name, string Relationship)
{
    /// <summary>The date of birth.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>The national ID number.</summary>
    public string? IdNumber { get; init; }

    /// <summary>A beneficiary's share in percent.</summary>
    public decimal? SharePercent { get; init; }

    /// <summary>A medical-aid dependant's member number.</summary>
    public string? MedicalAidNumber { get; init; }
}
