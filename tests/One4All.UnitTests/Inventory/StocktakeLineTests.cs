using FluentAssertions;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers the stocktake line variance arithmetic.</summary>
public sealed class StocktakeLineTests
{
    [Fact]
    public void Uncounted_line_has_no_variance()
    {
        StocktakeLine line = new(itemId: 1, systemQuantity: 100m, averageCost: 1.50m);

        line.IsCounted.Should().BeFalse();
        line.VarianceQuantity.Should().Be(0m);
        line.VarianceValue.Should().Be(0m);
    }

    [Fact]
    public void A_short_count_is_a_negative_variance_at_average_cost()
    {
        StocktakeLine line = new(1, 100m, 1.50m);
        line.SetCount(95m);

        line.IsCounted.Should().BeTrue();
        line.VarianceQuantity.Should().Be(-5m);
        line.VarianceValue.Should().Be(-7.50m);
    }

    [Fact]
    public void A_surplus_count_is_a_positive_variance()
    {
        StocktakeLine line = new(1, 100m, 2.00m);
        line.SetCount(103m);

        line.VarianceQuantity.Should().Be(3m);
        line.VarianceValue.Should().Be(6.00m);
    }
}
