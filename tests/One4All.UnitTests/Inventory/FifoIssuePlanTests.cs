using FluentAssertions;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers FIFO issue costing: oldest layers first, exact layer values, pro-rata partial takes and shortfalls.</summary>
public sealed class FifoIssuePlanTests
{
    private static List<StockCostLayer> Layers() =>
    [
        new(1, 1, 1, new DateOnly(2026, 3, 1), "R1", 10m, 50m),
        new(1, 1, 1, new DateOnly(2026, 3, 5), "R2", 10m, 80m),
    ];

    [Fact]
    public void Issues_consume_the_oldest_layers_first()
    {
        List<StockCostLayer> layers = Layers();
        FifoIssuePlan plan = FifoIssuePlan.For(layers, 15m, 0m);

        plan.Cost.Should().Be(90m);
        plan.Takes.Select(t => (t.Layer.Reference, t.Quantity, t.Cost)).Should().Equal(("R1", 10m, 50m), ("R2", 5m, 40m));
        layers[0].RemainingQuantity.Should().Be(10m, "planning changes nothing");

        plan.Apply();
        layers.Select(l => (l.RemainingQuantity, l.RemainingValue)).Should().Equal((0m, 0m), (5m, 40m));
    }

    [Fact]
    public void Quantity_beyond_the_layers_is_costed_at_the_fallback()
    {
        FifoIssuePlan plan = FifoIssuePlan.For(Layers(), 25m, 9m);

        plan.Shortfall.Should().Be(5m);
        plan.Cost.Should().Be(175m);
    }

    [Fact]
    public void A_partial_take_is_pro_rata_and_the_last_take_clears_the_layer_exactly()
    {
        StockCostLayer layer = new(1, 1, 1, new DateOnly(2026, 3, 1), "R1", 3m, 10m);

        layer.Take(1m).Should().Be(3.3333m);
        layer.Take(1m).Should().Be(3.3334m);
        layer.Take(1m).Should().Be(3.3333m);
        (layer.RemainingQuantity, layer.RemainingValue).Should().Be((0m, 0m));
    }
}
