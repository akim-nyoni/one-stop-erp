using One4All.Core.Common;

namespace One4All.Core.JobCosting;

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
}
