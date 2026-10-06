using FluentAssertions;
using One4All.Application.Inventory;
using One4All.Application.Pos;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the till's item limits and cost-plus price arithmetic.</summary>
public sealed class ItemSaleRulesTests
{
    private static readonly Dictionary<long, ItemSaleLimits> Limits = new()
    {
        [1] = new ItemSaleLimits(1, "BEER", 18, 6m, false),
        [2] = new ItemSaleLimits(2, "RX", null, null, true),
    };

    [Fact]
    public void Limits_apply_to_the_quantity_across_lines()
    {
        ItemSaleRules.Check([(1, 3m), (1, 3m), (3, 100m)], Limits, true, null).IsSuccess.Should().BeTrue();
        ItemSaleRules.Check([(1, 4m), (1, 3m)], Limits, true, null).Error.Code.Should().Be("Pos.MaxQuantity");
        ItemSaleRules.Check([(1, 1m)], Limits, false, null).Error.Code.Should().Be("Pos.AgeCheck");
        ItemSaleRules.Check([(2, 1m)], Limits, false, " ").Error.Code.Should().Be("Pos.PrescriptionRequired");
        ItemSaleRules.Check([(2, 1m)], Limits, false, "RX-1").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Prices_from_cost_by_markup_or_margin()
    {
        PriceAdjustment.FromCost(10m, CostPlusBasis.Markup, 25m, 0.05m, PriceRounding.Nearest).Should().Be(12.5m);
        PriceAdjustment.FromCost(8m, CostPlusBasis.Margin, 20m, 0m, PriceRounding.Nearest).Should().Be(10m);
        PriceAdjustment.FromCost(7m, CostPlusBasis.Margin, 30m, 1m, PriceRounding.Up).Should().Be(10m);
    }
}
