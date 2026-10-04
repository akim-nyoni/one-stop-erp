using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class EvictionWorkflowTests
{
    private static readonly DateOnly May10 = new(2026, 5, 10);

    [Fact]
    public void Stages_follow_in_order()
    {
        EvictionWorkflow.Problem(EvictionStage.Reminder, May10, null, 30, EvictionStage.Demand, May10.AddDays(7), null).Should().BeNull();
        EvictionWorkflow.Problem(EvictionStage.Reminder, May10, null, 30, EvictionStage.NoticeToVacate, May10.AddDays(7), null).Should().Contain("next stage is Demand");
        EvictionWorkflow.Problem(EvictionStage.Demand, May10, null, 30, EvictionStage.Reminder, May10, null).Should().NotBeNull();
    }

    [Fact]
    public void A_stage_cannot_be_back_dated()
    {
        EvictionWorkflow.Problem(EvictionStage.Reminder, May10, null, 30, EvictionStage.Demand, May10.AddDays(-1), null).Should().Contain("cannot be dated before");
    }

    [Fact]
    public void Legal_handover_waits_for_the_notice_period()
    {
        DateOnly notice = new(2026, 5, 24);
        EvictionWorkflow.Problem(EvictionStage.NoticeToVacate, notice, notice, 30, EvictionStage.LegalHandover, new DateOnly(2026, 6, 22), null).Should().Contain("runs until 2026-06-23");
        EvictionWorkflow.Problem(EvictionStage.NoticeToVacate, notice, notice, 30, EvictionStage.LegalHandover, new DateOnly(2026, 6, 23), null).Should().BeNull();
    }

    [Fact]
    public void A_court_order_needs_its_reference()
    {
        EvictionWorkflow.Problem(EvictionStage.LegalHandover, May10, null, 30, EvictionStage.CourtOrder, May10, " ").Should().Contain("reference");
        EvictionWorkflow.Problem(EvictionStage.LegalHandover, May10, null, 30, EvictionStage.CourtOrder, May10, "CO-1").Should().BeNull();
    }

    [Fact]
    public void Nothing_follows_eviction()
    {
        EvictionWorkflow.Problem(EvictionStage.Evicted, May10, null, 30, EvictionStage.Evicted, May10, null).Should().Contain("already been evicted");
    }
}
