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

    /// <summary>The waiting-list number, once registered (brief §4.10.3).</summary>
    public string? WaitingListNumber { get; private set; }

    /// <summary>The waiting-list category.</summary>
    public HousingCategory Category { get; private set; } = HousingCategory.HighDensity;

    /// <summary>The applicant's date of birth.</summary>
    public DateOnly? DateOfBirth { get; private set; }

    /// <summary>Since when the applicant has lived in the local authority area.</summary>
    public DateOnly? ResidentSince { get; private set; }

    /// <summary>The number of dependants.</summary>
    public int Dependants { get; private set; }

    /// <summary>Whether the applicant has a disability.</summary>
    public bool HasDisability { get; private set; }

    /// <summary>The applicant's monthly income.</summary>
    public decimal MonthlyIncome { get; private set; }

    /// <summary>Where the applicant lives now.</summary>
    public CurrentTenure Tenure { get; private set; } = CurrentTenure.Tenant;

    /// <summary>Where the applicant stands on the list.</summary>
    public ApplicantStatus Status { get; private set; } = ApplicantStatus.Active;

    /// <summary>The beneficiary groups the applicant declared and proved (age and disability groups are also derived).</summary>
    public BeneficiaryGroups DeclaredGroups { get; private set; }

    /// <summary>Records the beneficiary groups the applicant declared and proved.</summary>
    /// <param name="groups">The groups.</param>
    public void DeclareGroups(BeneficiaryGroups groups) => DeclaredGroups = groups;

    /// <summary>Marks the applicant verified.</summary>
    public void Verify() => IsVerified = true;

    /// <summary>Records the facts the qualification rules and the priority score use.</summary>
    /// <param name="category">The waiting-list category.</param>
    /// <param name="dateOfBirth">The date of birth.</param>
    /// <param name="residentSince">Since when resident in the area.</param>
    /// <param name="dependants">The number of dependants.</param>
    /// <param name="hasDisability">Whether the applicant has a disability.</param>
    /// <param name="monthlyIncome">The monthly income.</param>
    /// <param name="tenure">Where the applicant lives now.</param>
    public void SetDetails(HousingCategory category, DateOnly dateOfBirth, DateOnly residentSince, int dependants, bool hasDisability, decimal monthlyIncome, CurrentTenure tenure)
    {
        Category = category;
        DateOfBirth = dateOfBirth;
        ResidentSince = residentSince;
        Dependants = dependants;
        HasDisability = hasDisability;
        MonthlyIncome = monthlyIncome;
        Tenure = tenure;
    }

    /// <summary>
    /// Puts the applicant on the list under a waiting-list number from a registration date (a removed applicant who
    /// applies again starts afresh; the date sets seniority).
    /// </summary>
    /// <param name="waitingListNumber">The number.</param>
    /// <param name="registrationDate">The registration date.</param>
    public void Register(string waitingListNumber, DateOnly registrationDate)
    {
        WaitingListNumber = waitingListNumber;
        RegistrationDate = registrationDate;
        Status = ApplicantStatus.Active;
    }

    /// <summary>Changes where the applicant stands on the list.</summary>
    /// <param name="status">The new status.</param>
    public void SetStatus(ApplicantStatus status) => Status = status;

    /// <summary>When the applicant last confirmed they still want to stay on the list.</summary>
    public DateOnly? LastConfirmedOn { get; private set; }

    /// <summary>Records a renewal confirmation, putting a suspended applicant back on the list.</summary>
    /// <param name="date">When.</param>
    public void Confirm(DateOnly date)
    {
        LastConfirmedOn = date;
        if (Status == ApplicantStatus.Suspended)
        {
            Status = ApplicantStatus.Active;
        }
    }

    /// <summary>
    /// Changes the registration date — the date that sets seniority — under an approved dual-authorised change only.
    /// </summary>
    /// <param name="change">The approved change.</param>
    public void ChangeRegistrationDate(RegistrationDateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Status != ChangeRequestStatus.Approved || change.ApplicantId != Id)
        {
            throw new InvalidOperationException("Only an approved change for this applicant can move a registration date.");
        }

        RegistrationDate = change.NewDate;
    }
}
