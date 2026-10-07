using FluentAssertions;
using One4All.Core.System;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers fiscal calendars: monthly years from any month, the 4-4-5 week calendar, recognising a calendar, and the next year's code.</summary>
public sealed class FiscalCalendarTests
{
    [Fact]
    public void Monthly_years_start_in_any_month()
    {
        var periods = FiscalCalendars.Periods(FiscalCalendar.Monthly, new DateOnly(2026, 7, 1));
        periods.Should().HaveCount(12);
        periods[0].Should().Be((new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));
        periods[7].Should().Be((new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28)));
        periods[^1].End.Should().Be(new DateOnly(2027, 6, 30));
        FiscalCalendars.Detect(periods).Should().Be(FiscalCalendar.Monthly);
    }

    [Fact]
    public void Four_four_five_is_52_weeks()
    {
        var periods = FiscalCalendars.Periods(FiscalCalendar.FourFourFive, new DateOnly(2026, 3, 2));
        periods.Select(p => (p.End.DayNumber - p.Start.DayNumber + 1) / 7).Should().Equal(4, 4, 5, 4, 4, 5, 4, 4, 5, 4, 4, 5);
        periods.Zip(periods.Skip(1)).Should().OnlyContain(pair => pair.First.End.AddDays(1) == pair.Second.Start);
        (periods[^1].End.DayNumber - periods[0].Start.DayNumber + 1).Should().Be(364);
        FiscalCalendars.Detect(periods).Should().Be(FiscalCalendar.FourFourFive);
    }

    [Theory]
    [InlineData("2026", "2027")]
    [InlineData("2026/27", "2027/28")]
    [InlineData("FY26", "FY27")]
    [InlineData("FY99", "FY00")]
    [InlineData("Year", "Year2027")]
    public void The_next_code_moves_every_number_on(string code, string next) => FiscalCalendars.NextCode(code, new DateOnly(2027, 7, 1)).Should().Be(next);
}
