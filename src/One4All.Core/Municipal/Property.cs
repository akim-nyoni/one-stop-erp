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

    /// <summary>The number of dials (the register wraps to zero after 10^digits − 1); 0 when unknown.</summary>
    public int Digits { get; init; }

    /// <summary>When it was installed.</summary>
    public DateOnly? InstalledOn { get; init; }

    /// <summary>When it was removed.</summary>
    public DateOnly? RemovedOn { get; private set; }

    /// <summary>The date of the last reading, actual or estimated.</summary>
    public DateOnly? LastReadingDate { get; private set; }

    /// <summary>The last actual reading.</summary>
    public decimal LastActualReading { get; private set; }

    /// <summary>The date of the last actual reading.</summary>
    public DateOnly? LastActualDate { get; private set; }

    /// <summary>Consumption estimated (and billed) since the last actual reading.</summary>
    public decimal EstimatedSinceActual { get; private set; }

    /// <summary>Consumption over-estimated in the past, netted off the next consumption billed.</summary>
    public decimal UnitsCredit { get; private set; }

    /// <summary>
    /// Records a reading and returns the consumption since the previous reading (× multiplier); a lower reading counts as
    /// no consumption (validated capture goes through the meter reading service).
    /// </summary>
    /// <param name="reading">The new reading.</param>
    /// <param name="date">The reading date.</param>
    /// <returns>The consumption for the period.</returns>
    public decimal RecordReading(decimal reading, DateOnly date)
    {
        decimal consumption = Math.Max(0m, (reading - LastReading) * Multiplier);
        ApplyActual(reading, date, Math.Max(0m, UnitsCredit - consumption));
        return consumption;
    }

    /// <summary>Sets the reading a new meter starts from.</summary>
    /// <param name="reading">The start reading.</param>
    /// <param name="date">The installation date.</param>
    /// <param name="unitsCredit">Over-estimated consumption carried over from the meter it replaces.</param>
    public void Start(decimal reading, DateOnly date, decimal unitsCredit)
    {
        LastReading = reading;
        LastActualReading = reading;
        LastReadingDate = date;
        LastActualDate = date;
        UnitsCredit = unitsCredit;
    }

    /// <summary>Accepts an actual reading.</summary>
    /// <param name="reading">The reading.</param>
    /// <param name="date">The date.</param>
    /// <param name="unitsCreditAfter">The over-estimate still to net off.</param>
    public void ApplyActual(decimal reading, DateOnly date, decimal unitsCreditAfter)
    {
        LastReading = reading;
        LastActualReading = reading;
        LastReadingDate = date;
        LastActualDate = date;
        EstimatedSinceActual = 0m;
        UnitsCredit = unitsCreditAfter;
    }

    /// <summary>Records an estimated reading.</summary>
    /// <param name="reading">The estimated register value.</param>
    /// <param name="date">The date.</param>
    /// <param name="units">The consumption estimated.</param>
    public void ApplyEstimate(decimal reading, DateOnly date, decimal units)
    {
        LastReading = reading;
        LastReadingDate = date;
        EstimatedSinceActual += units;
    }

    /// <summary>Takes the meter out of service.</summary>
    /// <param name="date">The removal date.</param>
    public void Remove(DateOnly date)
    {
        IsActive = false;
        RemovedOn = date;
    }
}

/// <summary>Whether a reading was read off the meter or estimated.</summary>
public enum MeterReadingKind
{
    /// <summary>Read off the meter.</summary>
    Actual = 1,

    /// <summary>Estimated from past consumption.</summary>
    Estimated = 2,
}

/// <summary>What validation found about a reading.</summary>
[Flags]
public enum ReadingFlags
{
    /// <summary>Nothing unusual.</summary>
    None = 0,

    /// <summary>Consumption well above what the meter usually records.</summary>
    HighConsumption = 1,

    /// <summary>Consumption well below what the meter usually records.</summary>
    LowConsumption = 2,

    /// <summary>The register wrapped past zero.</summary>
    Rollover = 4,

    /// <summary>Lower than the last reading without a plausible wrap (rejected).</summary>
    Negative = 8,

    /// <summary>Tampering suspected by the reader (rejected pending investigation).</summary>
    Tamper = 16,

    /// <summary>Below what was estimated since the last actual reading: the excess is credited against later use.</summary>
    EstimateCorrection = 32,
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
        : this(companyId, meterId, readingDate, reading, previousReading, consumption, consumption, MeterReadingKind.Actual, ReadingFlags.None, accepted: true)
    {
    }

    /// <summary>Creates a validated or estimated reading.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="readingDate">The reading date.</param>
    /// <param name="reading">The reading value.</param>
    /// <param name="previousReading">The previous reading.</param>
    /// <param name="consumption">The consumption the meter recorded (× multiplier).</param>
    /// <param name="billedConsumption">The consumption to bill, after estimates and credits.</param>
    /// <param name="kind">Actual or estimated.</param>
    /// <param name="flags">What validation found.</param>
    /// <param name="accepted">Whether it was accepted (rejected readings leave the meter unchanged).</param>
    public MeterReading(long companyId, long meterId, DateOnly readingDate, decimal reading, decimal previousReading, decimal consumption,
        decimal billedConsumption, MeterReadingKind kind, ReadingFlags flags, bool accepted)
    {
        CompanyId = companyId;
        MeterId = meterId;
        ReadingDate = readingDate;
        Reading = reading;
        PreviousReading = previousReading;
        Consumption = consumption;
        BilledConsumption = billedConsumption;
        Kind = kind;
        Flags = flags;
        Accepted = accepted;
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

    /// <summary>The consumption to bill, after estimates and credits.</summary>
    public decimal BilledConsumption { get; private set; }

    /// <summary>Actual or estimated.</summary>
    public MeterReadingKind Kind { get; private set; }

    /// <summary>What validation found.</summary>
    public ReadingFlags Flags { get; private set; }

    /// <summary>Whether it was accepted.</summary>
    public bool Accepted { get; private set; }
}
