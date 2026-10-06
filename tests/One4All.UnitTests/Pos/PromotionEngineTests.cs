using FluentAssertions;
using One4All.Application.Pos;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the promotions engine: mix and match, lowest-price-free, buy X get Y, percent off, meal deals, priority and compounding.</summary>
public sealed class PromotionEngineTests
{
    private static PromotionRule Rule(string code, PromotionKind kind, int quantity = 0, int reward = 0, decimal? price = null, decimal? percent = null, long[]? qualifying = null,
        long[]? rewardItems = null, long[][]? courses = null, int priority = 10, bool compounding = false)
        => new(code, kind, priority, compounding, quantity, reward, price, percent, new HashSet<long>(qualifying ?? []), new HashSet<long>(rewardItems ?? []),
            [.. (courses ?? []).Select(c => (IReadOnlySet<long>)new HashSet<long>(c))], "4950");

    [Fact]
    public void Mix_and_match_sets_take_the_most_expensive_units_and_share_the_saving()
    {
        IReadOnlyList<LineDiscount> result = PromotionEngine.Apply([new BasketLine(1, 1, 2m, 5m), new BasketLine(2, 2, 2m, 4m)], [Rule("MIX3", PromotionKind.NForPrice, 3, price: 10m, qualifying: [1, 2])]);

        result.Should().Equal(new LineDiscount(1, "MIX3", "4950", 2.86m), new LineDiscount(2, "MIX3", "4950", 1.14m));
    }

    [Fact]
    public void Lowest_price_free_gives_the_cheapest_of_each_set()
    {
        IReadOnlyList<LineDiscount> result = PromotionEngine.Apply(
            [new BasketLine(1, 1, 1m, 10m), new BasketLine(2, 2, 1m, 6m), new BasketLine(3, 3, 1m, 3m), new BasketLine(4, 4, 1m, 2m)],
            [Rule("3FOR2", PromotionKind.BuyXGetY, 2, 1, percent: 100m, qualifying: [1, 2, 3, 4])]);

        result.Should().Equal(new LineDiscount(3, "3FOR2", "4950", 3m));
    }

    [Fact]
    public void Buy_x_get_y_discounts_the_cheapest_rewards_for_each_set_bought()
    {
        IReadOnlyList<LineDiscount> result = PromotionEngine.Apply([new BasketLine(1, 1, 4m, 8m), new BasketLine(2, 2, 3m, 3m)],
            [Rule("BURGER", PromotionKind.BuyXGetY, 2, 1, percent: 50m, qualifying: [1], rewardItems: [2])]);

        result.Should().Equal(new LineDiscount(2, "BURGER", "4950", 3m));
    }

    [Fact]
    public void Percent_off_covers_weighed_quantities()
    {
        PromotionEngine.Apply([new BasketLine(1, 7, 1.5m, 4m)], [Rule("FRUIT10", PromotionKind.PercentOff, percent: 10m, qualifying: [7])])
            .Should().Equal(new LineDiscount(1, "FRUIT10", "4950", 0.6m));
    }

    [Fact]
    public void Meal_deals_price_one_from_each_course()
    {
        IReadOnlyList<LineDiscount> result = PromotionEngine.Apply(
            [new BasketLine(1, 1, 1m, 10m), new BasketLine(2, 2, 1m, 12m), new BasketLine(3, 3, 1m, 4m), new BasketLine(4, 4, 1m, 3m)],
            [Rule("MEAL", PromotionKind.MealDeal, price: 15m, courses: [[1, 2], [3], [4]])]);

        result.Should().Equal(new LineDiscount(2, "MEAL", "4950", 2.53m), new LineDiscount(3, "MEAL", "4950", 0.84m), new LineDiscount(4, "MEAL", "4950", 0.63m));
    }

    [Fact]
    public void Priority_claims_units_and_compounding_stacks_on_what_is_left()
    {
        IReadOnlyList<LineDiscount> result = PromotionEngine.Apply([new BasketLine(1, 1, 3m, 5m)],
        [
            Rule("TWO8", PromotionKind.NForPrice, 2, price: 8m, qualifying: [1], priority: 1),
            Rule("TEN", PromotionKind.PercentOff, percent: 10m, qualifying: [1], priority: 2),
            Rule("STAFF5", PromotionKind.PercentOff, percent: 5m, qualifying: [1], priority: 3, compounding: true),
        ]);

        result.Should().Equal(new LineDiscount(1, "STAFF5", "4950", 0.63m), new LineDiscount(1, "TEN", "4950", 0.5m), new LineDiscount(1, "TWO8", "4950", 2m));
    }
}
