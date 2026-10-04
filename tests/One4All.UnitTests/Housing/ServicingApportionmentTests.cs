using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class ServicingApportionmentTests
{
    private static readonly StandMeasures[] Stands = [new(1, 300m, 15m, 1m), new(2, 600m, 20m, 1.5m), new(3, 300m, 15m, 1m)];

    [Theory]
    [InlineData(ServicingBasis.Equal, 3000.0, 3000.0, 3000.0)]
    [InlineData(ServicingBasis.ByArea, 2250.0, 4500.0, 2250.0)]
    [InlineData(ServicingBasis.ByFrontage, 2700.0, 3600.0, 2700.0)]
    [InlineData(ServicingBasis.ByZoningWeight, 2571.43, 3857.14, 2571.43)]
    public void Each_basis_shares_the_cost(ServicingBasis basis, double first, double second, double third)
    {
        ServicingApportionment.Apportion(Stands, basis, 9000m, out IReadOnlyList<StandShare> shares).Should().BeNull();
        shares.Select(s => s.Amount).Should().Equal((decimal)first, (decimal)second, (decimal)third);
        shares.Sum(s => s.Amount).Should().Be(9000m);
    }

    [Fact]
    public void Rounding_lands_on_the_largest_stand()
    {
        ServicingApportionment.Apportion([new(1, 1m, null, null), new(2, 1m, null, null), new(3, 1m, null, null)], ServicingBasis.Equal, 100m, out IReadOnlyList<StandShare> shares);
        shares.Select(s => s.Amount).Should().Equal(33.34m, 33.33m, 33.33m);
    }

    [Fact]
    public void A_missing_measure_is_named()
    {
        ServicingApportionment.Apportion([new(1, 300m, null, null)], ServicingBasis.ByFrontage, 100m, out _).Should().Contain("Stand 1").And.Contain("ByFrontage");
        ServicingApportionment.Apportion([], ServicingBasis.Equal, 100m, out _).Should().Contain("no stands");
    }
}
