using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

public class InstalmentScheduleTests
{
    private static readonly IReadOnlyList<Instalment> Schedule = InstalmentSchedule.Build(1000m, 300m, new DateOnly(2026, 1, 15));

    [Fact]
    public void Instalments_fall_monthly_with_a_smaller_last_one()
    {
        Schedule.Should().Equal(
            new Instalment(1, new DateOnly(2026, 1, 15), 300m),
            new Instalment(2, new DateOnly(2026, 2, 15), 300m),
            new Instalment(3, new DateOnly(2026, 3, 15), 300m),
            new Instalment(4, new DateOnly(2026, 4, 15), 100m));
    }

    [Fact]
    public void An_instalment_inside_its_grace_days_is_not_yet_missed()
    {
        ArrangementStanding standing = InstalmentSchedule.Assess(Schedule, 300m, new DateOnly(2026, 3, 20), 5, 2);
        standing.DueToDate.Should().Be(600m);
        standing.Shortfall.Should().Be(300m);
        standing.InstalmentsMissed.Should().Be(1);
        standing.NextDue.Should().Be(Schedule[1]);
        standing.Defaulted.Should().BeFalse();
    }

    [Fact]
    public void Enough_missed_instalments_default_the_arrangement()
    {
        ArrangementStanding standing = InstalmentSchedule.Assess(Schedule, 300m, new DateOnly(2026, 4, 25), 5, 2);
        standing.InstalmentsMissed.Should().Be(3);
        standing.Defaulted.Should().BeTrue();
    }

    [Fact]
    public void Paying_the_total_honours_it_even_after_missed_dates()
    {
        ArrangementStanding standing = InstalmentSchedule.Assess(Schedule, 1000m, new DateOnly(2026, 6, 1), 0, 1);
        standing.Honoured.Should().BeTrue();
        standing.Defaulted.Should().BeFalse();
        standing.NextDue.Should().BeNull();
    }

    [Fact]
    public void Paying_ahead_covers_later_instalments()
    {
        InstalmentSchedule.Assess(Schedule, 700m, new DateOnly(2026, 3, 1), 0, 1).NextDue.Should().Be(Schedule[2]);
    }

    [Fact]
    public void Nothing_to_pay_has_no_schedule()
    {
        InstalmentSchedule.Build(0m, 100m, new DateOnly(2026, 1, 1)).Should().BeEmpty();
    }
}
