using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers loyalty points earned, tiers and first-in-first-out expiry.</summary>
public sealed class LoyaltyRulesTests
{
    private static readonly (string Name, decimal MinPoints, decimal Multiplier)[] Tiers = [("Silver", 1000m, 1.5m), ("Gold", 5000m, 2m)];

    [Fact]
    public void Points_are_whole_and_multiplied_by_tier()
    {
        LoyaltyRules.Points(123.45m, 1m, 1m).Should().Be(123m);
        LoyaltyRules.Points(123.45m, 1m, 1.5m).Should().Be(185m);
        LoyaltyRules.Points(-5m, 1m, 1m).Should().Be(0m);
    }

    [Fact]
    public void The_highest_tier_reached_applies()
    {
        LoyaltyRules.Tier(999m, Tiers).Should().Be(((string?)null, 1m));
        LoyaltyRules.Tier(1000m, Tiers).Should().Be(("Silver", 1.5m));
        LoyaltyRules.Tier(7000m, Tiers).Should().Be(("Gold", 2m));
    }

    [Fact]
    public void Points_used_come_from_the_oldest_and_what_is_left_of_expired_lots_expires()
    {
        (decimal, DateOnly?)[] earned = [(100m, new DateOnly(2026, 1, 31)), (50m, new DateOnly(2026, 3, 31)), (30m, null)];

        LoyaltyRules.Expiring(earned, used: 60m, new DateOnly(2026, 2, 1)).Should().Be(40m);
        LoyaltyRules.Expiring(earned, used: 120m, new DateOnly(2026, 4, 1)).Should().Be(30m);
        LoyaltyRules.Expiring(earned, used: 0m, new DateOnly(2026, 1, 30)).Should().Be(0m);
    }
}
