using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

/// <summary>Covers the instalment default stages and penalty interest.</summary>
public sealed class InstalmentDefaultTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 30);

    [Fact]
    public void Days_overdue_follow_the_oldest_unpaid_instalment()
    {
        (DateOnly, decimal)[] raised = [(new DateOnly(2026, 4, 1), 100m), (new DateOnly(2026, 5, 1), 100m), (new DateOnly(2026, 6, 1), 100m)];
        InstalmentDefault.DaysOverdue(0m, raised, AsAt).Should().Be(0);
        InstalmentDefault.DaysOverdue(100m, raised, AsAt).Should().Be(29, "only June's instalment is unpaid");
        InstalmentDefault.DaysOverdue(150m, raised, AsAt).Should().Be(60, "May's is part-paid");
    }

    [Fact]
    public void Stages_run_in_order_each_after_the_last_is_served()
    {
        InstalmentDefault.Next(0, null, AsAt).Next.Should().BeNull();
        InstalmentDefault.Next(5, null, AsAt).Next.Should().Be(DefaultStage.Reminder);
        InstalmentDefault.Next(95, null, AsAt).Next.Should().Be(DefaultStage.Reminder, "never skip a stage");
        InstalmentDefault.Next(40, (DefaultStage.Reminder, new DateOnly(2026, 6, 1), null), AsAt).Why.Should().Contain("not been served");
        InstalmentDefault.Next(25, (DefaultStage.Reminder, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2)), AsAt).Why.Should().Contain("after 30 days");
        InstalmentDefault.Next(40, (DefaultStage.Reminder, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2)), AsAt).Next.Should().Be(DefaultStage.Demand);
        InstalmentDefault.Next(95, (DefaultStage.Breach, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)), AsAt).Why.Should().Contain("must stand 30 days");
        InstalmentDefault.Next(95, (DefaultStage.Breach, new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 12)), AsAt).Next.Should().Be(DefaultStage.Cancellation);
        InstalmentDefault.Next(120, (DefaultStage.Cancellation, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)), AsAt).Why.Should().Contain("cancel the agreement");
    }

    [Fact]
    public void Penalty_interest_is_a_twelfth_of_the_annual_rate()
    {
        InstalmentDefault.MonthlyPenalty(1200m, 18m).Should().Be(18m);
        InstalmentDefault.MonthlyPenalty(0m, 18m).Should().Be(0m);
        InstalmentDefault.MonthlyPenalty(1200m, 0m).Should().Be(0m);
        InstalmentDefault.Text(DefaultStage.Breach, "IA1-N3", "IA1", "Tendai", "S12", 300m, 65, AsAt).Should().StartWith("NOTICE OF BREACH").And.Contain("300.00");
    }
}
