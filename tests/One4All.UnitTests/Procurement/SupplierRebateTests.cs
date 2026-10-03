using FluentAssertions;
using One4All.Application.Procurement;
using Xunit;

namespace One4All.UnitTests.Procurement;

/// <summary>Covers retrospective tiered supplier rebates.</summary>
public sealed class SupplierRebateTests
{
    private static readonly RebateTier[] Tiers = [new(0m, 1m), new(10_000m, 2m), new(25_000m, 3m)];

    [Theory]
    [InlineData(5_000, 1, 50)]
    [InlineData(10_000, 2, 200)]       // crossing a tier lifts the rate on all spend
    [InlineData(30_000, 3, 900)]
    [InlineData(0, 1, 0)]
    public void The_highest_tier_reached_sets_the_rate_on_all_spend(int spend, int rate, int earned)
    {
        SupplierRebate.RateFor(spend, Tiers).Should().Be(rate);
        SupplierRebate.Earned(spend, Tiers).Should().Be(earned);
    }

    [Fact]
    public void Below_the_first_tier_nothing_is_earned()
    {
        RebateTier[] fromFiveThousand = [new(5_000m, 2m)];
        SupplierRebate.RateFor(4_000m, fromFiveThousand).Should().Be(0m);
        SupplierRebate.Earned(4_000m, fromFiveThousand).Should().Be(0m);
        SupplierRebate.Earned(-100m, Tiers).Should().Be(0m);
    }
}
