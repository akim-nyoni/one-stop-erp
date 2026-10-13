using One4All.Core.Common;

namespace One4All.Core.JobCosting;

/// <summary>Whether a weighed load arrives or leaves.</summary>
public enum WeighDirection
{
    /// <summary>Arriving loaded: gross first, tare second (supplier deliveries, returns).</summary>
    Inbound = 0,

    /// <summary>Leaving loaded: tare first, gross second (sales, deliveries).</summary>
    Outbound = 1,
}

/// <summary>A vehicle known to the weighbridge (<c>job.weighbridge_vehicle</c>).</summary>
public sealed class WeighbridgeVehicle : Entity, ICompanyScoped
{
    private WeighbridgeVehicle()
    {
        Registration = string.Empty;
    }

    /// <summary>Registers a vehicle.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="registration">The registration.</param>
    public WeighbridgeVehicle(long companyId, string registration)
    {
        CompanyId = companyId;
        Registration = registration.Trim().ToUpperInvariant();
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the registration.</summary>
    public string Registration { get; private set; }

    /// <summary>Gets or sets the haulier or owner.</summary>
    public string? Haulier { get; set; }

    /// <summary>Gets or sets the stored tare, used when the vehicle is not re-weighed empty.</summary>
    public decimal? StoredTareKg { get; set; }

    /// <summary>Gets or sets the legal maximum gross weight; heavier loads are flagged as overloads.</summary>
    public decimal? MaxGrossKg { get; set; }

    /// <summary>Gets or sets a value indicating whether the vehicle may use the weighbridge.</summary>
    public bool IsActive { get; set; }
}

/// <summary>A haulage charging band by distance (<c>job.haulage_zone</c>).</summary>
public sealed class HaulageZone : Entity, ICompanyScoped
{
    private HaulageZone()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a zone.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The zone code.</param>
    /// <param name="name">The name.</param>
    /// <param name="fromKm">The distance it starts at (inclusive).</param>
    /// <param name="toKm">The distance it runs to (exclusive).</param>
    /// <param name="ratePerTonne">The rate per tonne carried.</param>
    /// <param name="minimumCharge">The minimum charge per load.</param>
    public HaulageZone(long companyId, string code, string name, decimal fromKm, decimal toKm, decimal ratePerTonne, decimal minimumCharge)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        FromKm = fromKm;
        ToKm = toKm;
        RatePerTonne = ratePerTonne;
        MinimumCharge = minimumCharge;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the zone code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the distance the zone starts at (inclusive).</summary>
    public decimal FromKm { get; private set; }

    /// <summary>Gets the distance the zone runs to (exclusive).</summary>
    public decimal ToKm { get; private set; }

    /// <summary>Gets the rate per tonne.</summary>
    public decimal RatePerTonne { get; private set; }

    /// <summary>Gets the minimum charge per load.</summary>
    public decimal MinimumCharge { get; private set; }
}

/// <summary>
/// A weighbridge ticket for bulk materials — sand, stone, concrete (brief §3 weighbridge module). Captures
/// first weigh (gross) and second weigh (tare), computes net tonnage, and converts to m³ using a
/// per-product factor so bulk orders can be measured and invoiced in either unit.
/// </summary>
public sealed class WeighbridgeTicket : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WeighbridgeTicket()
    {
        TicketNumber = string.Empty;
        VehicleRegistration = string.Empty;
        Product = string.Empty;
    }

    /// <summary>Creates a ticket with the first (gross) weigh.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ticketNumber">The ticket number.</param>
    /// <param name="vehicleRegistration">The vehicle registration.</param>
    /// <param name="product">The product.</param>
    /// <param name="grossWeightKg">The gross weight (kg).</param>
    /// <param name="cubicMetresPerTonne">The m³-per-tonne conversion factor for the product.</param>
    /// <param name="date">The date.</param>
    public WeighbridgeTicket(long companyId, string ticketNumber, string vehicleRegistration, string product, decimal grossWeightKg, decimal cubicMetresPerTonne, DateOnly date)
    {
        CompanyId = companyId;
        TicketNumber = ticketNumber.Trim().ToUpperInvariant();
        VehicleRegistration = vehicleRegistration.Trim().ToUpperInvariant();
        Product = product;
        GrossWeightKg = grossWeightKg;
        CubicMetresPerTonne = cubicMetresPerTonne;
        Date = date;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The ticket number.</summary>
    public string TicketNumber { get; private set; }

    /// <summary>The vehicle registration.</summary>
    public string VehicleRegistration { get; private set; }

    /// <summary>The product (sand, stone, concrete …).</summary>
    public string Product { get; private set; }

    /// <summary>The gross weight (kg) — first weigh.</summary>
    public decimal GrossWeightKg { get; private set; }

    /// <summary>The tare weight (kg) — second weigh.</summary>
    public decimal TareWeightKg { get; private set; }

    /// <summary>Whether the second weigh has been captured.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>A manual moisture/deduction adjustment applied to the net (kg).</summary>
    public decimal DeductionKg { get; set; }

    /// <summary>The m³-per-tonne conversion factor for the product.</summary>
    public decimal CubicMetresPerTonne { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The net weight (kg) after tare and deductions, floored at zero.</summary>
    public decimal NetWeightKg => IsComplete ? Math.Max(0m, GrossWeightKg - TareWeightKg - DeductionKg) : 0m;

    /// <summary>The net weight in tonnes.</summary>
    public decimal NetTonnes => decimal.Round(NetWeightKg / 1000m, 4, MidpointRounding.ToEven);

    /// <summary>The net volume in cubic metres (tonnes × factor).</summary>
    public decimal NetCubicMetres => decimal.Round(NetTonnes * CubicMetresPerTonne, 4, MidpointRounding.ToEven);

    /// <summary>Captures the second (tare) weigh, completing the ticket.</summary>
    /// <param name="tareWeightKg">The tare weight (kg).</param>
    public void SecondWeigh(decimal tareWeightKg)
    {
        TareWeightKg = tareWeightKg;
        IsComplete = true;
    }

    /// <summary>Gets whether the load arrives (gross first) or leaves (tare first).</summary>
    public WeighDirection Direction { get; private set; }

    /// <summary>Gets or sets the product item, when the product is a stock item.</summary>
    public long? ItemId { get; set; }

    /// <summary>Gets or sets the customer the load is delivered to.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Gets or sets the supplier the load comes from.</summary>
    public long? SupplierId { get; set; }

    /// <summary>Gets or sets the driver.</summary>
    public string? DriverName { get; set; }

    /// <summary>Gets or sets the delivery site.</summary>
    public string? SiteName { get; set; }

    /// <summary>Gets or sets the stockpile (warehouse) the load comes from or goes to.</summary>
    public long? WarehouseId { get; set; }

    /// <summary>Gets or sets when the vehicle was first weighed (time in).</summary>
    public DateTimeOffset? TimeInUtc { get; set; }

    /// <summary>Gets when the vehicle was weighed the second time (time out).</summary>
    public DateTimeOffset? TimeOutUtc { get; private set; }

    /// <summary>Gets or sets a value indicating whether a weight was keyed by hand rather than read from the scale.</summary>
    public bool IsManualWeight { get; set; }

    /// <summary>Gets or sets why a weight was keyed by hand.</summary>
    public string? ManualReason { get; set; }

    /// <summary>Gets or sets the supervisor who authorised the manual weight.</summary>
    public long? ManualAuthorisedByUserId { get; set; }

    /// <summary>Gets or sets the operator who weighed.</summary>
    public long? OperatorUserId { get; set; }

    /// <summary>Gets or sets the haulage distance in km.</summary>
    public decimal? DistanceKm { get; set; }

    /// <summary>Gets or sets the haulage zone charged.</summary>
    public string? HaulageZoneCode { get; set; }

    /// <summary>Gets or sets the haulage charge.</summary>
    public decimal HaulageCharge { get; set; }

    /// <summary>Gets or sets the concrete batch (assembly run) the load came from.</summary>
    public string? BatchRunNumber { get; set; }

    /// <summary>Gets or sets the sales order the load is delivered against (its customer must match).</summary>
    public long? SalesOrderId { get; set; }

    /// <summary>Gets the sales invoice the load was billed on (null: not yet invoiced); a ticket is invoiced once.</summary>
    public string? InvoiceNumber { get; private set; }

    /// <summary>Marks the load billed on an invoice.</summary>
    /// <param name="invoiceNumber">The invoice number.</param>
    public void MarkInvoiced(string invoiceNumber) => InvoiceNumber = invoiceNumber;

    /// <summary>Starts an outbound ticket: the empty vehicle is weighed first (tare), loaded second (gross).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ticketNumber">The ticket number.</param>
    /// <param name="vehicleRegistration">The vehicle.</param>
    /// <param name="product">The product.</param>
    /// <param name="tareWeightKg">The empty weight.</param>
    /// <param name="cubicMetresPerTonne">The m³ per tonne conversion.</param>
    /// <param name="date">The ticket date.</param>
    /// <returns>The ticket.</returns>
    public static WeighbridgeTicket Outbound(long companyId, string ticketNumber, string vehicleRegistration, string product, decimal tareWeightKg, decimal cubicMetresPerTonne, DateOnly date)
        => new(companyId, ticketNumber, vehicleRegistration, product, 0m, cubicMetresPerTonne, date) { Direction = WeighDirection.Outbound, TareWeightKg = tareWeightKg };

    /// <summary>Completes the ticket with the second weight: the tare for an inbound load, the loaded gross for an outbound one.</summary>
    /// <param name="weightKg">The second weight.</param>
    /// <param name="atUtc">When.</param>
    public void Complete(decimal weightKg, DateTimeOffset atUtc)
    {
        if (Direction == WeighDirection.Outbound)
        {
            GrossWeightKg = weightKg;
        }
        else
        {
            TareWeightKg = weightKg;
        }

        TimeOutUtc = atUtc;
        IsComplete = true;
    }
}
