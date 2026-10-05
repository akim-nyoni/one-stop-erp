using FluentAssertions;
using One4All.Application.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

public sealed class BomRulesTests
{
    [Fact]
    public void Requirement_scales_to_output_and_adds_scrap()
    {
        BomRules.Requirement(5m, 10m, 10m, 9m).Should().Be(4.95m);
        BomRules.Requirement(1m, 0m, 10m, 25m).Should().Be(2.5m);
        BomRules.Requirement(1m, 0m, 0m, 25m).Should().Be(0m);
    }

    [Fact]
    public void Unit_cost_rolls_up_components()
        => BomRules.UnitCost([(5m, 10m, 1m), (1m, 0m, 4m)], 10m).Should().Be(0.95m);

    [Fact]
    public void Yield_is_actual_over_planned()
    {
        BomRules.Yield(10m, 9m).Should().Be(90.0m);
        BomRules.Yield(3m, 4m).Should().Be(133.3m);
        BomRules.Yield(0m, 4m).Should().BeNull();
    }
}
