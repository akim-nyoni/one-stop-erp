using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class TitlePipelineTests
{
    [Theory]
    [InlineData(TitleStage.Applied, TitleStage.ConveyancerAssigned)]
    [InlineData(TitleStage.ConveyancerAssigned, TitleStage.Lodged)]
    [InlineData(TitleStage.Lodged, TitleStage.QueryRaised)]
    [InlineData(TitleStage.QueryRaised, TitleStage.QueryResolved)]
    [InlineData(TitleStage.QueryResolved, TitleStage.QueryRaised)]
    [InlineData(TitleStage.Lodged, TitleStage.Registered)]
    [InlineData(TitleStage.QueryResolved, TitleStage.Registered)]
    [InlineData(TitleStage.Registered, TitleStage.InCustody)]
    [InlineData(TitleStage.InCustody, TitleStage.HandedOver)]
    public void The_pipeline_moves_forward_one_stage_at_a_time(TitleStage from, TitleStage to)
    {
        TitlePipeline.CanMove(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(TitleStage.Applied, TitleStage.Lodged)]
    [InlineData(TitleStage.QueryRaised, TitleStage.Registered)]
    [InlineData(TitleStage.Registered, TitleStage.HandedOver)]
    [InlineData(TitleStage.InCustody, TitleStage.Registered)]
    [InlineData(TitleStage.HandedOver, TitleStage.Applied)]
    [InlineData(TitleStage.Lodged, TitleStage.Lodged)]
    public void Stages_cannot_be_skipped_or_reversed(TitleStage from, TitleStage to)
    {
        TitlePipeline.CanMove(from, to).Should().BeFalse();
    }

    [Fact]
    public void Ageing_groups_open_applications_by_stage_in_pipeline_order()
    {
        DateOnly asAt = new(2026, 9, 1);
        TitleAge[] open =
        [
            new(TitleStage.Lodged, new DateOnly(2026, 8, 1), new DateOnly(2026, 6, 1)),
            new(TitleStage.Applied, new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 22)),
            new(TitleStage.Lodged, new DateOnly(2025, 9, 1), new DateOnly(2025, 7, 1)),
            new(TitleStage.HandedOver, new DateOnly(2026, 8, 30), new DateOnly(2025, 1, 1)),
        ];
        TitlePipeline.Ageing(open, asAt).Should().Equal(
            new StageAgeing(TitleStage.Applied, 1, 10, 10),
            new StageAgeing(TitleStage.Lodged, 2, 198, 365));
    }
}
