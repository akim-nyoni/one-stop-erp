using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>
/// A property / stand on the municipal register (brief §4.9, <c>mun.property</c>). Carries the valuation
/// used for rates and links to the ratepayer (a customer account) that services are billed to.
/// </summary>
public sealed class Property : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<PropertyServiceLink> _services = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Property()
    {
        StandNumber = string.Empty;
    }

    /// <summary>Creates a property.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="standNumber">The stand/erf number.</param>
    /// <param name="ward">The ward.</param>
    /// <param name="ratepayerCustomerId">The ratepayer (customer) account.</param>
    public Property(long companyId, string standNumber, string ward, long ratepayerCustomerId)
    {
        CompanyId = companyId;
        StandNumber = standNumber.Trim().ToUpperInvariant();
        Ward = ward;
        RatepayerCustomerId = ratepayerCustomerId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stand/erf number.</summary>
    public string StandNumber { get; private set; }

    /// <summary>The physical address.</summary>
    public string? Address { get; set; }

    /// <summary>The ward.</summary>
    public string Ward { get; set; } = string.Empty;

    /// <summary>The zone / suburb.</summary>
    public string? Zone { get; set; }

    /// <summary>The size in square metres.</summary>
    public decimal SizeSquareMetres { get; set; }

    /// <summary>The land use / zoning.</summary>
    public string? LandUse { get; set; }

    /// <summary>The current valuation (for rates).</summary>
    public decimal Valuation { get; set; }

    /// <summary>The ratepayer (customer) account billed for services.</summary>
    public long RatepayerCustomerId { get; set; }

    /// <summary>The services linked to this property.</summary>
    public IReadOnlyList<PropertyServiceLink> Services => _services.AsReadOnly();

    /// <summary>Links a service to the property.</summary>
    /// <param name="serviceType">The service.</param>
    /// <param name="tariffId">The tariff.</param>
    /// <param name="startDate">The start date.</param>
    /// <returns>The created link.</returns>
    public PropertyServiceLink AddService(ServiceType serviceType, long tariffId, DateOnly startDate)
    {
        PropertyServiceLink link = new(serviceType, tariffId, startDate);
        _services.Add(link);
        return link;
    }
}

/// <summary>A service linked to a property (<c>mun.property_service</c>).</summary>
public sealed class PropertyServiceLink : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PropertyServiceLink()
    {
    }

    /// <summary>Creates a service link.</summary>
    /// <param name="serviceType">The service.</param>
    /// <param name="tariffId">The tariff.</param>
    /// <param name="startDate">The start date.</param>
    public PropertyServiceLink(ServiceType serviceType, long tariffId, DateOnly startDate)
    {
        ServiceType = serviceType;
        TariffId = tariffId;
        StartDate = startDate;
        IsActive = true;
    }

    /// <summary>The owning property id.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The service type.</summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>The tariff applied.</summary>
    public long TariffId { get; private set; }

    /// <summary>The meter id, when metered.</summary>
    public long? MeterId { get; set; }

    /// <summary>The service start date.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The service end date, if any.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Whether the service is active.</summary>
    public bool IsActive { get; set; }
}

/// <summary>A meter on a property service (<c>mun.meter</c>).</summary>
public sealed class Meter : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Meter() => Serial = string.Empty;

    /// <summary>Creates a meter.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="serial">The meter serial.</param>
    /// <param name="multiplier">The reading multiplier.</param>
    public Meter(long companyId, string serial, decimal multiplier = 1m)
    {
        CompanyId = companyId;
        Serial = serial.Trim().ToUpperInvariant();
        Multiplier = multiplier;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The meter serial.</summary>
    public string Serial { get; private set; }

    /// <summary>The reading multiplier.</summary>
    public decimal Multiplier { get; private set; }

    /// <summary>The last reading recorded.</summary>
    public decimal LastReading { get; private set; }

    /// <summary>Whether the meter is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Records a reading and returns the consumption since the previous reading (× multiplier), handling
    /// meter rollover by treating a lower new reading as a wrap only when it is materially lower.
    /// </summary>
    /// <param name="reading">The new reading.</param>
    /// <returns>The consumption for the period.</returns>
    public decimal RecordReading(decimal reading)
    {
        decimal consumption = (reading - LastReading) * Multiplier;
        if (consumption < 0m)
        {
            consumption = 0m; // guard against a lower reading (correction / re-read); a real rollover rule is configurable
        }

        LastReading = reading;
        return consumption;
    }
}

/// <summary>A meter reading (<c>mun.meter_reading</c>).</summary>
public sealed class MeterReading : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private MeterReading()
    {
    }

    /// <summary>Creates a reading.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="readingDate">The reading date.</param>
    /// <param name="reading">The reading value.</param>
    /// <param name="previousReading">The previous reading.</param>
    /// <param name="consumption">The computed consumption.</param>
    public MeterReading(long companyId, long meterId, DateOnly readingDate, decimal reading, decimal previousReading, decimal consumption)
    {
        CompanyId = companyId;
        MeterId = meterId;
        ReadingDate = readingDate;
        Reading = reading;
        PreviousReading = previousReading;
        Consumption = consumption;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The meter.</summary>
    public long MeterId { get; private set; }

    /// <summary>The reading date.</summary>
    public DateOnly ReadingDate { get; private set; }

    /// <summary>The reading value.</summary>
    public decimal Reading { get; private set; }

    /// <summary>The previous reading.</summary>
    public decimal PreviousReading { get; private set; }

    /// <summary>The computed consumption.</summary>
    public decimal Consumption { get; private set; }
}
