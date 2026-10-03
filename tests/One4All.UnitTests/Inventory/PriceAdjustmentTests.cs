using FluentAssertions;
using One4All.Application.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers percentage price changes and price-point rounding.</summary>
public sealed class PriceAdjustmentTests
{
    [Theory]
    [InlineData(10.00, 5, 0, PriceRounding.Nearest, 10.50)]     // +5% to the cent
    [InlineData(9.99, 5, 0.05, PriceRounding.Nearest, 10.50)]   // 10.4895 → nearest 0.05
    [InlineData(9.99, 5, 0.05, PriceRounding.Down, 10.45)]
    [InlineData(9.99, 5, 0.50, PriceRounding.Up, 10.50)]
    [InlineData(12.00, -10, 1.00, PriceRounding.Nearest, 11.00)] // 10.80 → 11
    [InlineData(12.00, -10, 1.00, PriceRounding.Down, 10.00)]
    [InlineData(0.00, 25, 0.05, PriceRounding.Up, 0.00)]
    [InlineData(7.25, 0, 0.50, PriceRounding.Nearest, 7.50)]     // a 0% change still snaps to the price point
    public void Applies_percent_then_rounds_to_the_price_point(double price, double percent, double roundTo, PriceRounding rounding, double expected) =>
        PriceAdjustment.Apply((decimal)price, (decimal)percent, (decimal)roundTo, rounding).Should().Be((decimal)expected);

    [Fact]
    public void Refuses_a_reduction_of_100_percent_or_more()
    {
        Action act = () => PriceAdjustment.Apply(10m, -100m, 0m, PriceRounding.Nearest);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
