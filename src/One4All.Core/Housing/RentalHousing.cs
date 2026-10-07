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

    /// <summary>The most people who may live in it (tenant, occupants and lodgers); null: no limit set.</summary>
    public int? MaxOccupants { get; set; }

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
    private readonly List<TenancyRentChange> _rentChanges = [];
    private readonly List<TenancyOccupant> _occupants = [];

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

    /// <summary>Rent changes from reviews, each from the first of a month.</summary>
    public IReadOnlyList<TenancyRentChange> RentChanges => _rentChanges.AsReadOnly();

    /// <summary>The monthly rent in force on a date: the latest review effective by then, or the rent at letting.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The monthly rent.</returns>
    public decimal RentOn(DateOnly date)
        => _rentChanges.Where(c => c.EffectiveFrom <= date).OrderByDescending(c => c.EffectiveFrom).Select(c => (decimal?)c.MonthlyRent).FirstOrDefault() ?? MonthlyRent;

    /// <summary>Records a rent change from a review.</summary>
    /// <param name="effectiveFrom">The first day of the month it applies from.</param>
    /// <param name="monthlyRent">The new monthly rent.</param>
    public void ChangeRent(DateOnly effectiveFrom, decimal monthlyRent) => _rentChanges.Add(new TenancyRentChange(effectiveFrom, monthlyRent));

    /// <summary>The people living in the unit besides the tenant (household members), with when they moved in and out.</summary>
    public IReadOnlyList<TenancyOccupant> Occupants => _occupants.AsReadOnly();

    /// <summary>Adds an occupant.</summary>
    /// <param name="occupant">The occupant.</param>
    /// <returns>The occupant.</returns>
    public TenancyOccupant AddOccupant(TenancyOccupant occupant)
    {
        _occupants.Add(occupant);
        return occupant;
    }

    /// <summary>The occupants living there on a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>How many.</returns>
    public int OccupantsOn(DateOnly date) => _occupants.Count(o => o.MovedInOn <= date && (o.MovedOutOn is null || o.MovedOutOn > date));

    /// <summary>Ends it; the household moves out with the tenant.</summary>
    /// <param name="endDate">The last day.</param>
    public void End(DateOnly endDate)
    {
        EndDate = endDate;
        Status = TenancyStatus.Ended;
        foreach (TenancyOccupant occupant in _occupants.Where(o => o.MovedOutOn is null))
        {
            occupant.MoveOut(endDate.AddDays(1));
        }
    }
}

/// <summary>A rent change on a tenancy from a rent review (<c>hse.tenancy_rent_change</c>).</summary>
public sealed class TenancyRentChange : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TenancyRentChange()
    {
    }

    /// <summary>Records a change.</summary>
    /// <param name="effectiveFrom">The first day of the month it applies from.</param>
    /// <param name="monthlyRent">The new monthly rent.</param>
    public TenancyRentChange(DateOnly effectiveFrom, decimal monthlyRent)
    {
        EffectiveFrom = effectiveFrom;
        MonthlyRent = monthlyRent;
    }

    /// <summary>The tenancy.</summary>
    public long TenancyId { get; private set; }

    /// <summary>The first day of the month it applies from.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The new monthly rent.</summary>
    public decimal MonthlyRent { get; private set; }
}

/// <summary>A household member living in a let unit (<c>hse.tenancy_occupant</c>, brief §4.10.7 occupants list).</summary>
public sealed class TenancyOccupant : Entity
{
    private TenancyOccupant()
    {
        FullName = string.Empty;
        Relationship = string.Empty;
    }

    /// <summary>Records an occupant.</summary>
    /// <param name="fullName">The name.</param>
    /// <param name="idNumber">The national ID, if any.</param>
    /// <param name="relationship">Their relationship to the tenant.</param>
    /// <param name="movedInOn">When they moved in.</param>
    public TenancyOccupant(string fullName, string? idNumber, string relationship, DateOnly movedInOn)
    {
        FullName = fullName;
        IdNumber = idNumber;
        Relationship = relationship;
        MovedInOn = movedInOn;
    }

    /// <summary>The tenancy.</summary>
    public long TenancyId { get; private set; }

    /// <summary>The name.</summary>
    public string FullName { get; private set; }

    /// <summary>The national ID, if any.</summary>
    public string? IdNumber { get; private set; }

    /// <summary>Their relationship to the tenant.</summary>
    public string Relationship { get; private set; }

    /// <summary>When they moved in.</summary>
    public DateOnly MovedInOn { get; private set; }

    /// <summary>The first day they no longer live there.</summary>
    public DateOnly? MovedOutOn { get; private set; }

    /// <summary>Records the move out.</summary>
    /// <param name="date">The first day they no longer live there.</param>
    public void MoveOut(DateOnly date) => MovedOutOn = date;
}

/// <summary>Where a lodger permit stands.</summary>
public enum LodgerPermitStatus
{
    /// <summary>In force until it expires.</summary>
    Active = 0,

    /// <summary>Revoked (or ended with the tenancy).</summary>
    Revoked = 1,
}

/// <summary>
/// A permit for a tenant to take in a lodger (<c>hse.lodger_permit</c>, brief §4.10.6/§4.10.7 subletting and lodger rules): the lodger, the rooms let to them,
/// the period, and a monthly lodger fee billed with the rent.
/// </summary>
public sealed class LodgerPermit : AuditableEntity, ICompanyScoped
{
    private LodgerPermit()
    {
        PermitNumber = string.Empty;
        LodgerName = string.Empty;
    }

    /// <summary>Issues the permit.</summary>
    public LodgerPermit(long companyId, string permitNumber, long tenancyId, string lodgerName, string? idNumber, int rooms, decimal monthlyFee, string? feeAccountCode,
        DateOnly issuedOn, DateOnly expiresOn)
    {
        CompanyId = companyId;
        PermitNumber = permitNumber;
        TenancyId = tenancyId;
        LodgerName = lodgerName;
        IdNumber = idNumber;
        Rooms = rooms;
        MonthlyFee = monthlyFee;
        FeeAccountCode = feeAccountCode;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The permit number.</summary>
    public string PermitNumber { get; private set; }

    /// <summary>The tenancy.</summary>
    public long TenancyId { get; private set; }

    /// <summary>The lodger.</summary>
    public string LodgerName { get; private set; }

    /// <summary>The lodger's national ID, if any.</summary>
    public string? IdNumber { get; private set; }

    /// <summary>Rooms let to the lodger.</summary>
    public int Rooms { get; private set; }

    /// <summary>The monthly lodger fee (0: none).</summary>
    public decimal MonthlyFee { get; private set; }

    /// <summary>The income account the fee goes to.</summary>
    public string? FeeAccountCode { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The last day it is in force.</summary>
    public DateOnly ExpiresOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public LodgerPermitStatus Status { get; private set; }

    /// <summary>The last day it was in force, when revoked.</summary>
    public DateOnly? RevokedOn { get; private set; }

    /// <summary>Why it was revoked.</summary>
    public string? RevokedReason { get; private set; }

    /// <summary>The last day it is (or was) in force.</summary>
    public DateOnly LastDay => RevokedOn is DateOnly revoked && revoked < ExpiresOn ? revoked : ExpiresOn;

    /// <summary>Whether it is in force on a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>True when in force.</returns>
    public bool InForceOn(DateOnly date) => IssuedOn <= date && date <= LastDay;

    /// <summary>Whether it is in force at any time between two dates.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <returns>True when it overlaps.</returns>
    public bool InForceBetween(DateOnly from, DateOnly to) => IssuedOn <= to && LastDay >= from;

    /// <summary>Revokes it.</summary>
    /// <param name="lastDay">The last day in force.</param>
    /// <param name="reason">Why.</param>
    public void Revoke(DateOnly lastDay, string reason)
    {
        Status = LodgerPermitStatus.Revoked;
        RevokedOn = lastDay;
        RevokedReason = reason;
    }
}
