using FluentAssertions;
using One4All.Application.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers <see cref="LandedCost.Apportion"/>: by value or quantity, cent-exact, refusing an empty basis.</summary>
public sealed class LandedCostTests
{
    private static readonly LandedCostLine[] Lines = [new(10m, 100m), new(30m, 60m)];

    [Fact]
    public void Charges_follow_value()
    {
        LandedCost.Apportion(Lines, 32m, LandedCostBasis.Value).Should().Equal(20m, 12m);
    }

    [Fact]
    public void Charges_follow_quantity()
    {
        LandedCost.Apportion(Lines, 32m, LandedCostBasis.Quantity).Should().Equal(8m, 24m);
    }

    [Fact]
    public void Shares_always_add_up_to_the_charge()
    {
        decimal[] shares = LandedCost.Apportion([new(1m, 1m), new(1m, 1m), new(1m, 1m)], 10m, LandedCostBasis.Value);
        shares.Should().Equal(3.34m, 3.33m, 3.33m);
        shares.Sum().Should().Be(10m);
    }

    [Fact]
    public void An_empty_basis_is_refused()
    {
        FluentActions.Invoking(() => LandedCost.Apportion([new(0m, 0m)], 5m, LandedCostBasis.Value)).Should().Throw<ArgumentException>();
    }
}
