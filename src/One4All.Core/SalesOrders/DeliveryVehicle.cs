using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>
/// A vehicle in the delivery fleet (<c>sop.delivery_vehicle</c>, brief §4.2 delivery scheduling and vehicle loads): its registration, what it
/// carries at most by weight and volume, and how many drops it makes in a day. Deliveries planned on it are held to those limits.
/// </summary>
public sealed class DeliveryVehicle : Entity, ICompanyScoped
{
    private DeliveryVehicle()
    {
        Registration = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Registers a vehicle.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="registration">The registration (upper-cased).</param>
    /// <param name="description">What it is.</param>
    /// <param name="capacityKg">The most it carries, in kilograms.</param>
    public DeliveryVehicle(long companyId, string registration, string description, decimal capacityKg)
    {
        CompanyId = companyId;
        Registration = registration.Trim().ToUpperInvariant();
        Description = description.Trim();
        CapacityKg = capacityKg;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the registration.</summary>
    public string Registration { get; private set; }

    /// <summary>Gets or sets what the vehicle is.</summary>
    public string Description { get; set; }

    /// <summary>Gets or sets the most it carries, in kilograms.</summary>
    public decimal CapacityKg { get; set; }

    /// <summary>Gets or sets the most it carries, in cubic metres (null: not limited).</summary>
    public decimal? CapacityM3 { get; set; }

    /// <summary>Gets or sets the most drops it makes in a day (null: not limited).</summary>
    public int? MaxStops { get; set; }

    /// <summary>Gets or sets the usual driver.</summary>
    public string? DefaultDriver { get; set; }

    /// <summary>Gets or sets a value indicating whether deliveries may be planned on it.</summary>
    public bool IsActive { get; set; }
}
