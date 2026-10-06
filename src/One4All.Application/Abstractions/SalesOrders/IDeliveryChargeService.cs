using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>A delivery's charge as worked out.</summary>
/// <param name="Zone">The customer's zone.</param>
/// <param name="WeightKg">The weight sent.</param>
/// <param name="VolumeM3">The volume sent.</param>
/// <param name="GoodsValue">The value of the goods sent at the order prices.</param>
/// <param name="Charge">The charge (zero when free).</param>
public sealed record DeliveryChargeQuote(string Zone, decimal WeightKg, decimal VolumeM3, decimal GoodsValue, decimal Charge);

/// <summary>
/// Delivery charges by zone, weight and volume (brief §4.13): rates per zone, the charge for a delivery from its items' unit weights and
/// volumes and the customer's zone, and the charge invoiced to the customer once per delivery.
/// </summary>
public interface IDeliveryChargeService
{
    /// <summary>Saves a zone's rate (replacing the zone's earlier rate).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="zone">The zone.</param>
    /// <param name="baseCharge">The charge per delivery.</param>
    /// <param name="perKg">The charge per kilogram.</param>
    /// <param name="perCubicMetre">The charge per cubic metre.</param>
    /// <param name="freeAboveValue">The goods value delivery is free from, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rate id.</returns>
    Task<Result<long>> SaveRateAsync(long companyId, string zone, decimal baseCharge, decimal perKg, decimal perCubicMetre, decimal? freeAboveValue, CancellationToken ct = default);

    /// <summary>Works out a delivery's charge (<c>Sop.NoZone</c> / <c>Sop.NoZoneRate</c> when the customer's zone has no rate).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="deliveryReference">The delivery.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quote.</returns>
    Task<Result<DeliveryChargeQuote>> QuoteAsync(long companyId, string deliveryReference, CancellationToken ct = default);

    /// <summary>
    /// Invoices a delivery's charge to the customer through the sales ledger, once (<c>Sop.DeliveryCharged</c>); a free delivery has
    /// nothing to invoice (<c>Sop.FreeDelivery</c>). With a tax code the tax at its rate is added and posted to the tax account.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="deliveryReference">The delivery.</param>
    /// <param name="invoiceNumber">The invoice number.</param>
    /// <param name="date">The invoice date.</param>
    /// <param name="revenueAccountCode">The delivery income account.</param>
    /// <param name="taxCode">The tax code, if taxable.</param>
    /// <param name="taxAccountCode">The output tax account (with a tax code).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quote invoiced.</returns>
    Task<Result<DeliveryChargeQuote>> InvoiceAsync(long companyId, string deliveryReference, string invoiceNumber, DateOnly date, string revenueAccountCode, string? taxCode, string? taxAccountCode,
        CancellationToken ct = default);
}
