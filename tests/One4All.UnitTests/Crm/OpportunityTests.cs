using FluentAssertions;
using One4All.Core.Crm;
using Xunit;

namespace One4All.UnitTests.Crm;

public class OpportunityTests
{
    [Fact]
    public void New_opportunity_starts_as_a_lead_at_10_percent()
    {
        Opportunity o = new(1, "Fleet supply", "A. Buyer", 100_000m);
        o.Stage.Should().Be(OpportunityStage.Lead);
        o.WeightedValue.Should().Be(10_000m);
        o.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Moving_stage_updates_default_probability_and_weighted_value()
    {
        Opportunity o = new(1, "Fleet supply", "A. Buyer", 100_000m);
        o.MoveTo(OpportunityStage.Proposal);
        o.ProbabilityPercent.Should().Be(50m);
        o.WeightedValue.Should().Be(50_000m);
    }

    [Fact]
    public void Won_is_full_value_and_closed()
    {
        Opportunity o = new(1, "Fleet supply", "A. Buyer", 100_000m);
        o.MoveTo(OpportunityStage.Won);
        o.WeightedValue.Should().Be(100_000m);
        o.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Lost_is_zero_weighted_and_closed()
    {
        Opportunity o = new(1, "Fleet supply", "A. Buyer", 100_000m);
        o.MoveTo(OpportunityStage.Lost);
        o.WeightedValue.Should().Be(0m);
        o.IsOpen.Should().BeFalse();
    }
}
