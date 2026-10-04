using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class LevyCalculatorTests
{
    [Fact]
    public void A_flat_levy_ignores_size_and_price()
    {
        LevyCalculator.Amount(LevyBasis.Flat, 250m, 300m, null).Should().Be(250m);
    }

    [Fact]
    public void A_per_square_metre_levy_follows_the_stand_size()
    {
        LevyCalculator.Amount(LevyBasis.PerSquareMetre, 2.125m, 301m, 5000m).Should().Be(639.63m);
    }

    [Fact]
    public void A_percentage_levy_applies_to_the_price()
    {
        LevyCalculator.Amount(LevyBasis.PercentOfPrice, 5m, 300m, 10000m).Should().Be(500m);
    }

    [Fact]
    public void A_percentage_levy_without_a_price_cannot_be_computed()
    {
        LevyCalculator.Amount(LevyBasis.PercentOfPrice, 5m, 300m, null).Should().BeNull();
    }
}
