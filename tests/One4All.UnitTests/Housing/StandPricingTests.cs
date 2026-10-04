using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class StandPricingTests
{
    [Fact]
    public void The_price_builds_up_from_cost_overhead_levy_and_margin()
    {
        StandPricing.Build(10000m, 300m, new PricingTerms(10m, 500m, 2m, 20m, 0m)).Should().Be(new PriceBuildUp(10000m, 1000m, 1100m, 2200m, 14300m));
    }

    [Fact]
    public void Rounding_up_goes_to_the_margin()
    {
        StandPricing.Build(10000m, 300m, new PricingTerms(10m, 500m, 2m, 20m, 1000m)).Should().Be(new PriceBuildUp(10000m, 1000m, 1100m, 2900m, 15000m));
    }

    private static readonly PricingRuleKey SchemeRule = new(1, PricingScope.Scheme, 7, null, null, null, new DateOnly(2026, 1, 1));
    private static readonly PricingRuleKey LaterSchemeRule = new(2, PricingScope.Scheme, 7, null, null, null, new DateOnly(2026, 6, 1));
    private static readonly PricingRuleKey BandRule = new(3, PricingScope.SizeBand, 7, 500m, 1000m, null, new DateOnly(2026, 1, 1));
    private static readonly PricingRuleKey StandRule = new(4, PricingScope.Stand, null, null, null, 42, new DateOnly(2026, 3, 1));
    private static readonly PricingRuleKey[] Rules = [SchemeRule, LaterSchemeRule, BandRule, StandRule];

    [Fact]
    public void A_stand_rule_beats_a_size_band_which_beats_the_scheme()
    {
        StandPricing.Choose(Rules, 42, 7, 600m, new DateOnly(2026, 4, 1)).Should().Be(StandRule);
        StandPricing.Choose(Rules, 43, 7, 600m, new DateOnly(2026, 4, 1)).Should().Be(BandRule);
        StandPricing.Choose(Rules, 43, 7, 300m, new DateOnly(2026, 4, 1)).Should().Be(SchemeRule);
    }

    [Fact]
    public void Within_a_scope_the_latest_effective_rule_applies()
    {
        StandPricing.Choose(Rules, 43, 7, 300m, new DateOnly(2026, 6, 1)).Should().Be(LaterSchemeRule);
        StandPricing.Choose(Rules, 42, 7, 600m, new DateOnly(2026, 2, 1)).Should().Be(BandRule); // the stand rule is not yet in effect
    }

    [Fact]
    public void Nothing_prices_a_stand_before_any_rule_or_in_another_scheme()
    {
        StandPricing.Choose(Rules, 43, 7, 300m, new DateOnly(2025, 12, 31)).Should().BeNull();
        StandPricing.Choose(Rules, 43, 8, 300m, new DateOnly(2026, 4, 1)).Should().BeNull();
    }
}
