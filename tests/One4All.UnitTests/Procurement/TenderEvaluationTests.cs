using FluentAssertions;
using One4All.Application.Procurement;
using Xunit;

namespace One4All.UnitTests.Procurement;

/// <summary>Covers tender scoring: points relative to the best price and lead time, weighting and ranking.</summary>
public sealed class TenderEvaluationTests
{
    [Fact]
    public void Quotes_earn_points_relative_to_the_best_price_and_lead_time()
    {
        IReadOnlyList<TenderScore> scores = TenderEvaluation.Score([new TenderBid(1, 1_000m, 20), new TenderBid(2, 800m, 30), new TenderBid(3, 1_000m, 10)]);

        // Supplier 2: cheapest (70) but 30 days against the best 10 (10) = 80. Supplier 3: 56 + 30 = 86. Supplier 1: 56 + 15 = 71.
        scores.Select(s => (s.SupplierId, s.PriceScore, s.LeadTimeScore, s.Score, s.Rank)).Should().Equal(
            (3L, 56m, 30m, 86m, 1),
            (2L, 70m, 10m, 80m, 2),
            (1L, 56m, 15m, 71m, 3));
    }

    [Fact]
    public void Price_alone_decides_when_it_carries_all_the_weight()
    {
        TenderEvaluation.Score([new TenderBid(1, 1_000m, 5), new TenderBid(2, 900m, 60)], priceWeight: 100m)[0].SupplierId.Should().Be(2);
    }

    [Fact]
    public void Equal_scores_rank_the_cheaper_quote_first()
    {
        IReadOnlyList<TenderScore> scores = TenderEvaluation.Score([new TenderBid(1, 500m, 10), new TenderBid(2, 500m, 10)]);
        scores.Select(s => s.Rank).Should().Equal(1, 2);
        TenderEvaluation.Score([]).Should().BeEmpty();
    }
}
