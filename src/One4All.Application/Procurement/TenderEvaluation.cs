namespace One4All.Application.Procurement;

/// <summary>A quote to score.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Total">The quote's total price.</param>
/// <param name="LeadTimeDays">The promised lead time.</param>
public readonly record struct TenderBid(long SupplierId, decimal Total, int LeadTimeDays);

/// <summary>A scored quote.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Total">The quote's total price.</param>
/// <param name="LeadTimeDays">The promised lead time.</param>
/// <param name="PriceScore">Points for price: the cheapest quote gets the full price weight.</param>
/// <param name="LeadTimeScore">Points for lead time: the quickest quote gets the full lead-time weight.</param>
/// <param name="Rank">Its place, 1 being best.</param>
public readonly record struct TenderScore(long SupplierId, decimal Total, int LeadTimeDays, decimal PriceScore, decimal LeadTimeScore, int Rank)
{
    /// <summary>The total score out of 100.</summary>
    public decimal Score => PriceScore + LeadTimeScore;
}

/// <summary>
/// Scores tender quotes for the comparison matrix (brief §4.3): price and lead time each earn points in proportion to the
/// best quote's (lowest price, shortest lead time), weighted to total 100. Ties rank by price, then lead time. Pure and
/// unit-tested.
/// </summary>
public static class TenderEvaluation
{
    /// <summary>The usual weighting: price 70, lead time 30.</summary>
    public const decimal DefaultPriceWeight = 70m;

    /// <summary>Scores and ranks quotes.</summary>
    /// <param name="bids">The quotes.</param>
    /// <param name="priceWeight">The points for price, out of 100; the rest are for lead time.</param>
    /// <returns>The scored quotes, best first.</returns>
    public static IReadOnlyList<TenderScore> Score(IReadOnlyList<TenderBid> bids, decimal priceWeight = DefaultPriceWeight)
    {
        ArgumentNullException.ThrowIfNull(bids);
        if (bids.Count == 0)
        {
            return [];
        }

        decimal leadWeight = 100m - priceWeight;
        decimal cheapest = bids.Min(b => b.Total);
        int quickest = bids.Min(b => b.LeadTimeDays);
        List<(TenderBid Bid, decimal Price, decimal Lead)> scored = [.. bids.Select(b => (
            b,
            b.Total <= 0m ? priceWeight : decimal.Round(priceWeight * cheapest / b.Total, 2, MidpointRounding.AwayFromZero),
            b.LeadTimeDays <= 0 ? leadWeight : decimal.Round(leadWeight * Math.Max(quickest, 0) / b.LeadTimeDays, 2, MidpointRounding.AwayFromZero)))];
        return [.. scored
            .OrderByDescending(s => s.Price + s.Lead).ThenBy(s => s.Bid.Total).ThenBy(s => s.Bid.LeadTimeDays)
            .Select((s, i) => new TenderScore(s.Bid.SupplierId, s.Bid.Total, s.Bid.LeadTimeDays, s.Price, s.Lead, i + 1))];
    }
}
