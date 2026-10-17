using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A sectional title scheme (<c>hse.sectional_scheme</c>, brief §4.10.6 sectional title / flats): a building on a stand
/// divided into units, the rest common property; each unit carries a participation quota that apportions the body
/// corporate's levies, and units - not the stand - are what is owned and transferred.
/// </summary>
public sealed class SectionalScheme : AuditableEntity, ICompanyScoped
{
    private readonly List<SectionalUnit> _units = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SectionalScheme()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Registers a scheme.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The scheme code.</param>
    /// <param name="name">The building's name.</param>
    /// <param name="standId">The stand the building is on.</param>
    public SectionalScheme(long companyId, string code, string name, long standId)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        StandId = standId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme code.</summary>
    public string Code { get; private set; }

    /// <summary>The building's name.</summary>
    public string Name { get; private set; }

    /// <summary>The stand the building is on.</summary>
    public long StandId { get; private set; }

    /// <summary>Its units.</summary>
    public IReadOnlyList<SectionalUnit> Units => _units.AsReadOnly();

    /// <summary>Adds a unit.</summary>
    /// <param name="unit">The unit.</param>
    public void AddUnit(SectionalUnit unit) => _units.Add(unit);
}

/// <summary>A unit in a sectional scheme (<c>hse.sectional_unit</c>).</summary>
public sealed class SectionalUnit : Entity
{
    private readonly List<UnitOwnership> _owners = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SectionalUnit()
    {
        UnitNumber = string.Empty;
    }

    /// <summary>Records a unit.</summary>
    /// <param name="unitNumber">The unit number.</param>
    /// <param name="floorArea">Its floor area in m².</param>
    /// <param name="participationQuota">Its participation quota (%).</param>
    public SectionalUnit(string unitNumber, decimal floorArea, decimal participationQuota)
    {
        UnitNumber = unitNumber;
        FloorArea = floorArea;
        ParticipationQuota = participationQuota;
    }

    /// <summary>The scheme.</summary>
    public long SectionalSchemeId { get; private set; }

    /// <summary>The unit number.</summary>
    public string UnitNumber { get; private set; }

    /// <summary>Its floor area in m².</summary>
    public decimal FloorArea { get; private set; }

    /// <summary>Its participation quota (%).</summary>
    public decimal ParticipationQuota { get; private set; }

    /// <summary>Its owners over time (append-only).</summary>
    public IReadOnlyList<UnitOwnership> Owners => _owners.AsReadOnly();

    /// <summary>The current owner's account, if it has one.</summary>
    public long? CurrentOwnerCustomerId => _owners.FirstOrDefault(o => o.OwnedTo is null)?.CustomerId;

    /// <summary>Passes the unit to a new owner, closing the previous ownership the day before.</summary>
    /// <param name="customerId">The new owner.</param>
    /// <param name="date">From when.</param>
    /// <param name="consideration">The consideration.</param>
    public void Transfer(long customerId, DateOnly date, decimal consideration)
    {
        _owners.FirstOrDefault(o => o.OwnedTo is null)?.End(date.AddDays(-1));
        _owners.Add(new UnitOwnership(customerId, date, consideration));
    }
}

/// <summary>An owner of a sectional unit for a period (<c>hse.unit_ownership</c>).</summary>
public sealed class UnitOwnership : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UnitOwnership()
    {
    }

    /// <summary>Records an owner.</summary>
    /// <param name="customerId">The owner.</param>
    /// <param name="ownedFrom">From when.</param>
    /// <param name="consideration">The consideration.</param>
    public UnitOwnership(long customerId, DateOnly ownedFrom, decimal consideration)
    {
        CustomerId = customerId;
        OwnedFrom = ownedFrom;
        Consideration = consideration;
    }

    /// <summary>The unit.</summary>
    public long SectionalUnitId { get; private set; }

    /// <summary>The owner.</summary>
    public long CustomerId { get; private set; }

    /// <summary>From when.</summary>
    public DateOnly OwnedFrom { get; private set; }

    /// <summary>Until when (inclusive); current while null.</summary>
    public DateOnly? OwnedTo { get; private set; }

    /// <summary>The consideration.</summary>
    public decimal Consideration { get; private set; }

    /// <summary>Ends the ownership.</summary>
    /// <param name="date">The last day owned.</param>
    public void End(DateOnly date) => OwnedTo = date;
}

/// <summary>A body corporate levy raised for a period (<c>hse.sectional_levy_run</c>); once per scheme per period.</summary>
public sealed class SectionalLevyRun : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SectionalLevyRun()
    {
    }

    /// <summary>Records a levy run.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="sectionalSchemeId">The scheme.</param>
    /// <param name="period">The period (yyyyMM).</param>
    /// <param name="levy">The levy for the period.</param>
    /// <param name="billed">The amount invoiced to owners.</param>
    /// <param name="runOn">When.</param>
    public SectionalLevyRun(long companyId, long sectionalSchemeId, int period, decimal levy, decimal billed, DateOnly runOn)
    {
        CompanyId = companyId;
        SectionalSchemeId = sectionalSchemeId;
        Period = period;
        Levy = levy;
        Billed = billed;
        RunOn = runOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SectionalSchemeId { get; private set; }

    /// <summary>The period (yyyyMM).</summary>
    public int Period { get; private set; }

    /// <summary>The levy for the period.</summary>
    public decimal Levy { get; private set; }

    /// <summary>The amount invoiced to owners (the rest fell on unowned units).</summary>
    public decimal Billed { get; private set; }

    /// <summary>When.</summary>
    public DateOnly RunOn { get; private set; }
}
