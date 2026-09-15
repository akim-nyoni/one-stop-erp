using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The delivery model of a housing scheme (brief §4.10.8).</summary>
public enum SchemeType
{
    /// <summary>Council-delivered.</summary>
    CouncilDelivered = 1,

    /// <summary>Public-private partnership / joint venture.</summary>
    PublicPrivatePartnership = 2,

    /// <summary>Housing cooperative.</summary>
    Cooperative = 3,

    /// <summary>Employer-assisted.</summary>
    EmployerAssisted = 4,

    /// <summary>Rural / peri-urban.</summary>
    RuralPeriUrban = 5,
}

/// <summary>
/// A housing scheme / project (brief §4.10.8, <c>hse.scheme</c>) — the organising unit that groups stands,
/// budgets and delivery milestones. Separately licensable from Municipality (brief §4.10).
/// </summary>
public sealed class HousingScheme : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private HousingScheme()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a scheme.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The scheme code.</param>
    /// <param name="name">The scheme name.</param>
    /// <param name="schemeType">The delivery model.</param>
    public HousingScheme(long companyId, string code, string name, SchemeType schemeType)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        SchemeType = schemeType;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme code.</summary>
    public string Code { get; private set; }

    /// <summary>The scheme name.</summary>
    public string Name { get; set; }

    /// <summary>The delivery model.</summary>
    public SchemeType SchemeType { get; set; }

    /// <summary>The target beneficiary count.</summary>
    public int TargetBeneficiaries { get; set; }
}

/// <summary>
/// A housing applicant on the waiting list (brief §4.10.3, <c>hse.applicant</c>). The registration date is
/// the queue position and is permission-protected; verification flags duplicate/ghost applicants.
/// </summary>
public sealed class HousingApplicant : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private HousingApplicant()
    {
        IdNumber = string.Empty;
        FullName = string.Empty;
    }

    /// <summary>Creates an applicant.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="idNumber">The national ID number (dedup key).</param>
    /// <param name="fullName">The full name.</param>
    /// <param name="registrationDate">The waiting-list registration date.</param>
    public HousingApplicant(long companyId, string idNumber, string fullName, DateOnly registrationDate)
    {
        CompanyId = companyId;
        IdNumber = idNumber.Trim().ToUpperInvariant();
        FullName = fullName;
        RegistrationDate = registrationDate;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The national ID number (dedup key).</summary>
    public string IdNumber { get; private set; }

    /// <summary>The full name.</summary>
    public string FullName { get; set; }

    /// <summary>The waiting-list registration date (queue position).</summary>
    public DateOnly RegistrationDate { get; private set; }

    /// <summary>Whether the applicant has been verified.</summary>
    public bool IsVerified { get; private set; }

    /// <summary>Marks the applicant verified.</summary>
    public void Verify() => IsVerified = true;
}
