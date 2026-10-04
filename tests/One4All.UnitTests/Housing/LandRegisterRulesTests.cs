using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class LandRegisterRulesTests
{
    [Fact]
    public void Part_of_a_parcel_takes_its_share_of_cost_and_the_whole_takes_the_rest()
    {
        LandRegisterRules.CostOfArea(3000m, 10000m, 1000m).Should().Be(3333.33m);
        LandRegisterRules.CostOfArea(2000m, 6666.67m, 2000m).Should().Be(6666.67m);
    }

    [Theory]
    [InlineData(LandDisputeStage.Reported, LandDisputeStage.Litigation, true)]
    [InlineData(LandDisputeStage.Mediation, LandDisputeStage.Negotiation, false)]
    [InlineData(LandDisputeStage.Litigation, LandDisputeStage.Litigation, false)]
    [InlineData(LandDisputeStage.Judgment, LandDisputeStage.Resolved, true)]
    [InlineData(LandDisputeStage.Resolved, LandDisputeStage.Litigation, false)]
    public void Disputes_only_move_forward(LandDisputeStage from, LandDisputeStage to, bool allowed)
        => LandRegisterRules.CanMove(from, to).Should().Be(allowed);

    [Fact]
    public void A_dispute_ages_until_it_is_resolved()
    {
        DateOnly opened = new(2026, 1, 1);
        LandRegisterRules.AgeInDays(opened, null, new DateOnly(2026, 3, 1)).Should().Be(59);
        LandRegisterRules.AgeInDays(opened, new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 1)).Should().Be(30);
    }
}
