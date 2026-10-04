using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class SchemeProgressRulesTests
{
    [Fact]
    public void Nrv_is_price_less_costs_to_complete_and_sell_and_only_a_shortfall_is_written_down()
    {
        SchemeProgressRules.Assess(10000m, 10000m, 500m, 5m).Should().Be(new NrvAssessment(500m, 9000m, 1000m));
        SchemeProgressRules.Assess(8000m, 10000m, 0m, 5m).WriteDown.Should().Be(0m);
    }

    [Fact]
    public void A_stand_is_serviced_only_when_every_service_reaches_it()
    {
        Dictionary<BulkService, int> reached = new() { [BulkService.Water] = 10, [BulkService.Sewer] = 8, [BulkService.Roads] = 12, [BulkService.Electricity] = 9 };
        SchemeProgressRules.ServicedStands(reached, 12).Should().Be(0);
        reached[BulkService.StormWater] = 11;
        SchemeProgressRules.ServicedStands(reached, 12).Should().Be(8);
    }

    [Fact]
    public void Survey_stages_only_move_forward()
    {
        SchemeProgressRules.CanAdvance(null, SurveyStage.DiagramLodged).Should().BeTrue();
        SchemeProgressRules.CanAdvance(SurveyStage.DiagramLodged, SurveyStage.Pegged).Should().BeFalse();
        SchemeProgressRules.CanAdvance(SurveyStage.Pegged, SurveyStage.Pegged).Should().BeFalse();
    }
}
