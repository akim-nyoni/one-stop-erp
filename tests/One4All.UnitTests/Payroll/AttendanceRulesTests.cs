using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

public sealed class AttendanceRulesTests
{
    [Fact]
    public void Hours_worked_wrap_overnight_and_need_both_times()
    {
        AttendanceRules.HoursWorked(new TimeOnly(8, 15), new TimeOnly(23, 30)).Should().Be(15.25m);
        AttendanceRules.HoursWorked(new TimeOnly(22, 0), new TimeOnly(6, 0)).Should().Be(8m);
        AttendanceRules.HoursWorked(new TimeOnly(8, 0), null).Should().BeNull();
    }

    [Fact]
    public void Overtime_cost_is_hours_at_basic_rate_times_multiplier()
    {
        AttendanceRules.OvertimeCost(2m, 1_733.30m, 173.33m, 1.5m).Should().Be(30m);
        AttendanceRules.OvertimeCost(3m, 1_000m, 173.33m, 2m).Should().Be(34.62m);
        AttendanceRules.OvertimeCost(3m, 1_000m, 0m, 2m).Should().Be(0m);
    }

    [Theory]
    [InlineData(90, "Full pay")]
    [InlineData(91, "Half pay")]
    [InlineData(180, "Half pay")]
    [InlineData(181, "Exhausted")]
    public void Sick_tier_steps_down(int days, string expected) => AttendanceRules.SickTier(days, 90m, 90m).Should().Be(expected);
}
