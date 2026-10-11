namespace One4All.Application.Abstractions.Procurement;

/// <summary>A supplier's performance over a period.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Its code.</param>
/// <param name="SupplierName">Its name.</param>
/// <param name="Deliveries">Goods received notes in the period.</param>
/// <param name="DeliveriesJudged">Deliveries against orders that had a required-by date.</param>
/// <param name="DeliveriesOnTime">Of those, the ones received by the date.</param>
/// <param name="QuantityReceived">Units received.</param>
/// <param name="QuantityReturned">Units returned to the supplier.</param>
/// <param name="Spend">Invoiced spend in base currency.</param>
/// <param name="OnTimePercent">Deliveries on time as a percentage of those judged; null when none could be.</param>
/// <param name="QualityPercent">Units kept as a percentage of those received; null when nothing was received.</param>
/// <param name="PriceVariance">Invoiced above (positive) or below the order prices on lines matched in the period.</param>
/// <param name="PriceVariancePercent">The variance as a percentage of the matched lines at order prices; null when nothing was matched.</param>
public sealed record SupplierScore(
    long SupplierId, string SupplierCode, string SupplierName, int Deliveries, int DeliveriesJudged, int DeliveriesOnTime,
    decimal QuantityReceived, decimal QuantityReturned, decimal Spend, decimal? OnTimePercent, decimal? QualityPercent, decimal PriceVariance = 0m, decimal? PriceVariancePercent = null);

/// <summary>
/// The supplier performance scorecard (brief §4.3): on-time delivery against orders' required-by dates, quality as the
/// share of goods not returned, and spend, for every supplier active in a period.
/// </summary>
public interface ISupplierScorecardService
{
    /// <summary>Scores every supplier with deliveries, returns or invoices in the period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scores, by supplier code.</returns>
    Task<IReadOnlyList<SupplierScore>> ScorecardAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
