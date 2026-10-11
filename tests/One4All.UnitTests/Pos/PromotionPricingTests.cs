using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers pricing a basket from detached promotion definitions (as the till does offline): dates, happy hour, coupons and category promotions.</summary>
public sealed class PromotionPricingTests
{
    private static PromotionDefinition Percent(string code, decimal percent, IReadOnlyList<PromotionItemRef> items, string? category = null, string? coupon = null,
        TimeOnly? from = null, TimeOnly? to = null, DateOnly? ends = null)
        => new(code, code, PromotionKind.PercentOff, 1, false, 1, 0, null, percent, category, false, null, coupon, new DateOnly(2026, 5, 1), ends, from, to, null, "4020", items);

    [Fact]
    public void Offline_prices_follow_the_same_rules_as_online()
    {
        BasketRequest basket = new(1, null, new DateTime(2026, 5, 10, 18, 30, 0), false, [], [new BasketItem(10, 2m, 5m), new BasketItem(20, 1m, 10m)]);
        Dictionary<long, string?> categories = new() { [10] = "BEV", [20] = "FOOD" };

        BasketPricing priced = PromotionPricing.Price([Percent("BEV10", 10m, [], category: "BEV")], basket, categories);
        priced.Lines.Select(l => l.Discount).Should().Equal(1m, 0m);
        priced.Applied.Should().ContainSingle(a => a.Code == "BEV10" && a.Saving == 1m);

        PromotionPricing.Price([Percent("HAPPY", 50m, [new PromotionItemRef(20, 0)], from: new TimeOnly(17, 0), to: new TimeOnly(19, 0))], basket, categories)
            .Lines[1].Discount.Should().Be(5m, "happy hour runs at 18:30");
        PromotionPricing.Price([Percent("LATE", 50m, [new PromotionItemRef(20, 0)], from: new TimeOnly(22, 0), to: new TimeOnly(2, 0))], basket, categories)
            .Applied.Should().BeEmpty("not yet 22:00");
        PromotionPricing.Price([Percent("ENDED", 50m, [new PromotionItemRef(20, 0)], ends: new DateOnly(2026, 5, 9))], basket, categories).Applied.Should().BeEmpty();
        PromotionPricing.Price([Percent("COUP", 20m, [new PromotionItemRef(20, 0)], coupon: "SAVE20")], basket, categories).Applied.Should().BeEmpty("no coupon entered");
        PromotionPricing.Price([Percent("COUP", 20m, [new PromotionItemRef(20, 0)], coupon: "SAVE20")], basket with { Coupons = ["save20"] }, categories)
            .Lines[1].Discount.Should().Be(2m);
    }

    [Fact]
    public void Promotions_survive_the_offline_snapshot()
    {
        One4All.Application.Abstractions.Offline.PosOfflineSnapshot snapshot = new()
        {
            Promotions = [Percent("HAPPY", 50m, [new PromotionItemRef(20, 0)], from: new TimeOnly(17, 0), to: new TimeOnly(19, 0), ends: new DateOnly(2026, 5, 31))], BranchId = 7,
        };
        System.Text.Json.JsonSerializerOptions web = new(System.Text.Json.JsonSerializerDefaults.Web);
        One4All.Application.Abstractions.Offline.PosOfflineSnapshot back = System.Text.Json.JsonSerializer.Deserialize<One4All.Application.Abstractions.Offline.PosOfflineSnapshot>(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot, web), web)!;
        back.BranchId.Should().Be(7);
        back.Promotions.Should().ContainSingle().Which.Should().BeEquivalentTo(snapshot.Promotions[0]);
    }
}
