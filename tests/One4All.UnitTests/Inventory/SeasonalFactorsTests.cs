using FluentAssertions;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers reading monthly seasonal factors and picking the month's factor.</summary>
public sealed class SeasonalFactorsTests
{
    [Fact]
    public void Twelve_factors_are_read_and_the_months_one_applies()
    {
        SeasonalFactors.TryParse("0.5, 0.5, 1, 1, 1, 1, 1, 1, 1, 1.5, 2, 3", out decimal[]? factors).Should().BeTrue();
        factors![11].Should().Be(3m);
        SeasonalFactors.For("0.5,0.5,1,1,1,1,1,1,1,1.5,2,3", new DateOnly(2026, 12, 10)).Should().Be(3m);
        SeasonalFactors.For(null, new DateOnly(2026, 12, 10)).Should().Be(1m);
        SeasonalFactors.Format(factors).Should().Be("0.5,0.5,1,1,1,1,1,1,1,1.5,2,3");
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("1,1,1,1,1,1,1,1,1,1,1,-1")]
    [InlineData("1,1,1,1,1,1,1,1,1,1,1,x")]
    public void Anything_but_twelve_sensible_factors_is_refused(string text)
        => SeasonalFactors.TryParse(text, out _).Should().BeFalse();
}
