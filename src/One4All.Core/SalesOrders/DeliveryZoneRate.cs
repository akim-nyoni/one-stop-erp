using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>
/// What a delivery to a zone costs (<c>sop.delivery_zone_rate</c>, brief §4.13): a base charge plus a rate per kilogram and per cubic
/// metre of the goods sent, free above an order value when set.
/// </summary>
public sealed class DeliveryZoneRate : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DeliveryZoneRate() => Zone = string.Empty;

    /// <summary>Creates the rate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="zone">The zone.</param>
    public DeliveryZoneRate(long companyId, string zone)
    {
        CompanyId = companyId;
        Zone = zone.Trim().ToUpperInvariant();
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The zone.</summary>
    public string Zone { get; private set; }

    /// <summary>Gets or sets the charge per delivery.</summary>
    public decimal BaseCharge { get; set; }

    /// <summary>Gets or sets the charge per kilogram.</summary>
    public decimal PerKg { get; set; }

    /// <summary>Gets or sets the charge per cubic metre.</summary>
    public decimal PerCubicMetre { get; set; }

    /// <summary>Gets or sets the goods value at or above which delivery is free.</summary>
    public decimal? FreeAboveValue { get; set; }

    /// <summary>The charge for a delivery.</summary>
    /// <param name="weightKg">The weight sent.</param>
    /// <param name="volumeM3">The volume sent.</param>
    /// <param name="goodsValue">The value of the goods sent.</param>
    /// <returns>The charge (zero when free).</returns>
    public decimal ChargeFor(decimal weightKg, decimal volumeM3, decimal goodsValue)
        => FreeAboveValue is decimal free && goodsValue >= free ? 0m : decimal.Round(BaseCharge + (weightKg * PerKg) + (volumeM3 * PerCubicMetre), 2, MidpointRounding.AwayFromZero);
}
