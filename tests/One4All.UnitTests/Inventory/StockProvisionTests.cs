using FluentAssertions;
using One4All.Application.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers <see cref="StockProvision.Assess"/>: NRV write-downs, slow-moving and obsolete bands, the larger wins.</summary>
public sealed class StockProvisionTests
{
    private static readonly StockProvisionPolicy Policy = StockProvisionPolicy.Default; // 10% to sell; 25% @180d; 100% @365d

    [Fact]
    public void Profitable_moving_stock_needs_no_provision()
    {
        StockProvision.Assess(new StockHolding(10m, 100m, 20m, 30), Policy)
            .Should().Be(new StockProvisionResult(180m, 0m, ProvisionBasis.None));
    }

    [Fact]
    public void Stock_that_will_sell_below_cost_is_written_down_to_nrv()
    {
        // NRV = 10 × 9 × 0.9 = 81 against a cost of 100.
        StockProvision.Assess(new StockHolding(10m, 100m, 9m, 30), Policy)
            .Should().Be(new StockProvisionResult(81m, 19m, ProvisionBasis.NetRealisableValue));
    }

    [Fact]
    public void Slow_moving_and_obsolete_bands_apply_to_cost()
    {
        StockProvision.Assess(new StockHolding(10m, 100m, 20m, 200), Policy).Should().Be(new StockProvisionResult(180m, 25m, ProvisionBasis.SlowMoving));
        StockProvision.Assess(new StockHolding(10m, 100m, null, 400), Policy).Should().Be(new StockProvisionResult(null, 100m, ProvisionBasis.Obsolete));
    }

    [Fact]
    public void The_larger_of_nrv_and_ageing_applies()
    {
        // NRV write-down 19 vs slow-moving 25 → 25; NRV write-down 55 (NRV 45) vs slow-moving 25 → 55.
        StockProvision.Assess(new StockHolding(10m, 100m, 9m, 200), Policy).Basis.Should().Be(ProvisionBasis.SlowMoving);
        StockProvision.Assess(new StockHolding(10m, 100m, 5m, 200), Policy).Should().Be(new StockProvisionResult(45m, 55m, ProvisionBasis.NetRealisableValue));
    }

    [Fact]
    public void Empty_or_costless_holdings_are_ignored()
    {
        StockProvision.Assess(new StockHolding(0m, 0m, 5m, 400), Policy).Provision.Should().Be(0m);
    }
}
