using FluentAssertions;
using One4All.Application.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers splitting a cost over parts by weight.</summary>
public sealed class BomSplitCostTests
{
    [Fact]
    public void Shares_follow_the_weights_and_sum_to_the_total()
    {
        BomRules.SplitCost(160m, [20m, 120m]).Should().Equal(22.86m, 137.14m);
        BomRules.SplitCost(100m, [1m, 1m, 1m]).Should().Equal(33.33m, 33.33m, 33.34m);
        BomRules.SplitCost(10m, [0m, 0m]).Should().Equal(5m, 5m);
        BomRules.SplitCost(10m, []).Should().BeEmpty();
    }
}
