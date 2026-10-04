using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The kind of council rental unit (brief §4.10.7).</summary>
public enum RentalUnitType
{
    /// <summary>A house.</summary>
    House = 1,

    /// <summary>A flat.</summary>
    Flat = 2,

    /// <summary>A room.</summary>
    Room = 3,

    /// <summary>A hostel bed.</summary>
    HostelBed = 4,

    /// <summary>A cottage.</summary>
    Cottage = 5,
}

/// <summary>
/// A council rental unit (<c>hse.rental_unit</c>, brief §4.10.7): housing stock held to earn rent — kept out of the
/// stands inventory — with its monthly rent and, while vacant, the date it fell void.
/// </summary>
public sealed class RentalUnit : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RentalUnit()
    {
        Code = string.Empty;
        Description = string.Empty;
        RentAccountCode = string.Empty;
    }

    /// <summary>Registers a unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The unit code.</param>
    /// <param name="description">The address or description.</param>
    /// <param name="unitType">The kind of unit.</param>
    /// <param name="rooms">The number of rooms.</param>
    /// <param name="monthlyRent">The monthly rent.</param>
    /// <param name="rentAccountCode">The rental income account.</param>
    /// <param name="vacantSince">The first day it has been empty.</param>
    public RentalUnit(long companyId, string code, string description, RentalUnitType unitType, int rooms, decimal monthlyRent, string rentAccountCode, DateOnly vacantSince)
    {
        CompanyId = companyId;
        Code = code;
        Description = description;
        UnitType = unitType;
        Rooms = rooms;
        MonthlyRent = monthlyRent;
        RentAccountCode = rentAccountCode;
        VacantSince = vacantSince;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The unit code.</summary>
    public string Code { get; private set; }

    /// <summary>The address or description.</summary>
    public string Description { get; private set; }

    /// <summary>The kind of unit.</summary>
    public RentalUnitType UnitType { get; private set; }

    /// <summary>The number of rooms.</summary>
    public int Rooms { get; private set; }

    /// <summary>The monthly rent.</summary>
    public decimal MonthlyRent { get; set; }

    /// <summary>The rental income account.</summary>
    public string RentAccountCode { get; private set; }

    /// <summary>The first day it has been empty, or null while let.</summary>
    public DateOnly? VacantSince { get; private set; }

    /// <summary>Whether it is let.</summary>
    public bool IsLet => VacantSince is null;

    /// <summary>Lets it.</summary>
    public void Let() => VacantSince = null;

    /// <summary>Marks it vacant from a date.</summary>
    /// <param name="date">The first empty day.</param>
    public void Vacate(DateOnly date) => VacantSince = date;
}

/// <summary>Where a tenancy stands.</summary>
public enum TenancyStatus
{
    /// <summary>Running.</summary>
    Active = 0,

    /// <summary>Ended; the unit was returned.</summary>
    Ended = 1,
}

/// <summary>
/// A tenancy of a council rental unit (<c>hse.tenancy</c>, brief §4.10.7): the tenant, the rent at letting, the deposit
/// held as a liability (never income) and the dates. A unit has one active tenancy at a time.
/// </summary>
public sealed class Tenancy : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Tenancy()
    {
        TenancyNumber = string.Empty;
        DepositAccountCode = string.Empty;
    }

    /// <summary>Starts a tenancy.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="tenancyNumber">The tenancy number.</param>
    /// <param name="unitId">The unit.</param>
    /// <param name="tenantCustomerId">The tenant's account.</param>
    /// <param name="startDate">The first day of the tenancy.</param>
    /// <param name="monthlyRent">The monthly rent.</param>
    /// <param name="deposit">The deposit held.</param>
    /// <param name="depositAccountCode">The liability account the deposit is held on.</param>
    public Tenancy(long companyId, string tenancyNumber, long unitId, long tenantCustomerId, DateOnly startDate, decimal monthlyRent, decimal deposit, string depositAccountCode)
    {
        CompanyId = companyId;
        TenancyNumber = tenancyNumber;
        UnitId = unitId;
        TenantCustomerId = tenantCustomerId;
        StartDate = startDate;
        MonthlyRent = monthlyRent;
        Deposit = deposit;
        DepositAccountCode = depositAccountCode;
        Status = TenancyStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The tenancy number.</summary>
    public string TenancyNumber { get; private set; }

    /// <summary>The unit.</summary>
    public long UnitId { get; private set; }

    /// <summary>The tenant's account.</summary>
    public long TenantCustomerId { get; private set; }

    /// <summary>The first day of the tenancy.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The last day of the tenancy, once ended.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>The monthly rent.</summary>
    public decimal MonthlyRent { get; private set; }

    /// <summary>The deposit held.</summary>
    public decimal Deposit { get; private set; }

    /// <summary>The liability account the deposit is held on.</summary>
    public string DepositAccountCode { get; private set; }

    /// <summary>Where it stands.</summary>
    public TenancyStatus Status { get; private set; }

    /// <summary>Ends it.</summary>
    /// <param name="endDate">The last day.</param>
    public void End(DateOnly endDate)
    {
        EndDate = endDate;
        Status = TenancyStatus.Ended;
    }
}
