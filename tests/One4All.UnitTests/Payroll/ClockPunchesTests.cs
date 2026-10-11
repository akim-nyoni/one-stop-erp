using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers reading a time-clock export: punches paired into days, unreadable lines reported, overtime beyond the shift.</summary>
public sealed class ClockPunchesTests
{
    [Fact]
    public void Punches_pair_into_days_first_in_last_out()
    {
        const string export = "Badge,Punch\ne001,2026-05-04 07:58\nE001,2026-05-04 12:30\nE001,2026-05-04 18:15\nE002;04/05/2026 08:20\nE002,2026-05-05,08:00\nrubbish\n";
        IReadOnlyList<ClockDay> days = ClockPunches.Days(export, out IReadOnlyList<string> problems);
        days.Should().Equal(
            new ClockDay("E001", new DateOnly(2026, 5, 4), new TimeOnly(7, 58), new TimeOnly(18, 15)),
            new ClockDay("E002", new DateOnly(2026, 5, 4), new TimeOnly(8, 20), null),
            new ClockDay("E002", new DateOnly(2026, 5, 5), new TimeOnly(8, 0), null));
        problems.Should().ContainSingle().Which.Should().StartWith("Line 7");
        days[0].HoursWorked.Should().Be(10.28m);

        ClockPunches.Overtime(days[0], new TimeOnly(8, 0), new TimeOnly(17, 0)).Should().Be(1.28m, "a nine-hour shift");
        ClockPunches.Overtime(days[0], null, null).Should().Be(0m, "no shift rostered");
        ClockPunches.Overtime(days[1], new TimeOnly(8, 0), new TimeOnly(17, 0)).Should().Be(0m, "no clock-out");
        ClockPunches.Overtime(new ClockDay("N", new DateOnly(2026, 5, 4), new TimeOnly(21, 0), new TimeOnly(23, 30)), new TimeOnly(22, 0), new TimeOnly(6, 0)).Should().Be(0m);
    }
}
