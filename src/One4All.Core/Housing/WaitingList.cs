using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The waiting-list category an applicant is registered under (brief §4.10.3).</summary>
public enum HousingCategory
{
    /// <summary>High-density stands.</summary>
    HighDensity = 1,

    /// <summary>Medium-density stands.</summary>
    MediumDensity = 2,

    /// <summary>Low-density stands.</summary>
    LowDensity = 3,

    /// <summary>Flats.</summary>
    Flats = 4,

    /// <summary>Council rental housing.</summary>
    Rental = 5,
}

/// <summary>Where an applicant lives now.</summary>
public enum CurrentTenure
{
    /// <summary>A lodger.</summary>
    Lodger = 1,

    /// <summary>A tenant.</summary>
    Tenant = 2,

    /// <summary>Homeless.</summary>
    Homeless = 3,

    /// <summary>Staying with relatives.</summary>
    WithRelatives = 4,

    /// <summary>In an informal settlement (scored as homeless: no secure shelter).</summary>
    InformalSettlement = 5,
}

/// <summary>Where an applicant stands on the waiting list.</summary>
public enum ApplicantStatus
{
    /// <summary>On the list.</summary>
    Active = 0,

    /// <summary>Suspended (for example for not renewing).</summary>
    Suspended = 1,

    /// <summary>Removed from the list.</summary>
    Removed = 2,

    /// <summary>Allocated a stand or unit.</summary>
    Allocated = 3,
}

/// <summary>
/// A council's waiting-list policy (<c>hse.waiting_list_policy</c>, brief §4.10.3: qualification rules and score weights
/// configurable, never hard-coded): who qualifies, and the points each factor earns. One per council.
/// </summary>
public sealed class WaitingListPolicy : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WaitingListPolicy()
    {
    }

    /// <summary>Creates a council's policy.</summary>
    /// <param name="companyId">The council.</param>
    public WaitingListPolicy(long companyId) => CompanyId = companyId;

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The minimum age to register.</summary>
    public int MinimumAge { get; set; } = 18;

    /// <summary>The minimum years of residence in the area.</summary>
    public int MinimumResidencyYears { get; set; }

    /// <summary>The highest monthly income that qualifies, or null for no limit.</summary>
    public decimal? MaximumMonthlyIncome { get; set; }

    /// <summary>Points per full year on the list.</summary>
    public decimal PointsPerYearOnList { get; set; } = 10m;

    /// <summary>Points per full year of residence in the area.</summary>
    public decimal PointsPerYearResident { get; set; } = 2m;

    /// <summary>Points per dependant.</summary>
    public decimal PointsPerDependant { get; set; } = 5m;

    /// <summary>Points for a disability.</summary>
    public decimal DisabilityPoints { get; set; } = 15m;

    /// <summary>Points for being homeless.</summary>
    public decimal HomelessPoints { get; set; } = 20m;

    /// <summary>Points for lodging or staying with relatives.</summary>
    public decimal LodgerPoints { get; set; } = 8m;

    /// <summary>Points per full year of the applicant's age above <see cref="AgePointsFromYears"/> (0: age carries no weight).</summary>
    public decimal PointsPerYearOfAge { get; set; }

    /// <summary>The age from which age points start.</summary>
    public int AgePointsFromYears { get; set; }

    /// <summary>Points for an applicant who belongs to a registered housing cooperative.</summary>
    public decimal CooperativeMemberPoints { get; set; }

    /// <summary>Only first-time buyers qualify: nobody who has ever held a council stand or owned property on the municipal register.</summary>
    public bool FirstTimeBuyersOnly { get; set; }

    /// <summary>Nobody whose earlier stand was repossessed or whose agreement was cancelled qualifies.</summary>
    public bool RefuseForfeited { get; set; }

    /// <summary>Applicants must be clear of overdue rates and charges.</summary>
    public bool RequireRatesClearance { get; set; }

    /// <summary>Council rental units are let only to applicants on the rental waiting list.</summary>
    public bool RentalFromWaitingList { get; set; }
}

/// <summary>
/// An income ceiling for one waiting-list category (<c>hse.waiting_list_income_ceiling</c>, brief §4.10.3 "maximum income for a category"):
/// applicants for the category earning more do not qualify. It applies alongside the policy's overall ceiling.
/// </summary>
public sealed class WaitingListIncomeCeiling : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WaitingListIncomeCeiling()
    {
    }

    /// <summary>Sets a ceiling.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="category">The category.</param>
    /// <param name="maximumMonthlyIncome">The highest monthly income that qualifies.</param>
    public WaitingListIncomeCeiling(long companyId, HousingCategory category, decimal maximumMonthlyIncome)
    {
        CompanyId = companyId;
        Category = category;
        MaximumMonthlyIncome = maximumMonthlyIncome;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The category.</summary>
    public HousingCategory Category { get; private set; }

    /// <summary>The highest monthly income that qualifies.</summary>
    public decimal MaximumMonthlyIncome { get; set; }
}
