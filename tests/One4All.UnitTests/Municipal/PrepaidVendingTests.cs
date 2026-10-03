using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class PrepaidVendingTests
{
    [Fact]
    public void A_share_of_the_purchase_pays_down_arrears()
    {
        PrepaidVending.Split(50m, 100m, 20m, 0.25m).Should().Be(new VendSplit(10m, 40m, 160m));
    }

    [Fact]
    public void Never_more_than_is_owed_goes_to_arrears()
    {
        PrepaidVending.Split(500m, 90m, 25m, 0.25m).Should().Be(new VendSplit(90m, 410m, 1640m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Without_arrears_everything_buys_units(int balance)
    {
        PrepaidVending.Split(20m, balance, 25m, 0.25m).Should().Be(new VendSplit(0m, 20m, 80m));
    }

    [Fact]
    public void Units_round_down_so_the_council_never_over_issues()
    {
        PrepaidVending.Split(10m, 0m, 0m, 0.3m).Units.Should().Be(33.33m);
    }

    [Fact]
    public void A_full_arrears_rule_can_take_the_whole_purchase()
    {
        PrepaidVending.Split(50m, 80m, 100m, 0.25m).Should().Be(new VendSplit(50m, 0m, 0m));
    }
}
