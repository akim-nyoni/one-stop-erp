using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class DeveloperDeliveryTests
{
    private static readonly DateOnly Deadline = new(2026, 4, 30);

    [Theory]
    [InlineData(2026, 4, 30, 0)]
    [InlineData(2026, 4, 1, 0)]
    [InlineData(2026, 5, 1, 1)]
    [InlineData(2026, 5, 30, 1)]
    [InlineData(2026, 5, 31, 2)]
    [InlineData(2026, 6, 15, 2)]
    [InlineData(2026, 7, 10, 3)]
    public void Every_month_or_part_month_counts(int year, int month, int day, int expected)
    {
        DeveloperDelivery.MonthsLate(Deadline, new DateOnly(year, month, day)).Should().Be(expected);
    }

    [Fact]
    public void Month_ends_are_handled()
    {
        DeveloperDelivery.MonthsLate(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28)).Should().Be(1);
        DeveloperDelivery.MonthsLate(new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 1)).Should().Be(2);
    }

    [Fact]
    public void Outstanding_stands_accrue_to_the_assessment_date_and_delivered_ones_stop()
    {
        // Three owed: one on time, one a month late, one still outstanding two (part) months on.
        decimal penalty = DeveloperDelivery.Penalty(3, [new DateOnly(2026, 4, 20), new DateOnly(2026, 5, 20)], Deadline, new DateOnly(2026, 6, 15), 100m);
        penalty.Should().Be(300m);
    }

    [Fact]
    public void Hand_overs_after_the_assessment_date_are_not_yet_counted()
    {
        DeveloperDelivery.Penalty(1, [new DateOnly(2026, 8, 1)], Deadline, new DateOnly(2026, 6, 15), 50m).Should().Be(100m);
    }

    [Fact]
    public void Nothing_accrues_before_the_deadline()
    {
        DeveloperDelivery.Penalty(5, [], Deadline, new DateOnly(2026, 4, 30), 100m).Should().Be(0m);
    }
}
