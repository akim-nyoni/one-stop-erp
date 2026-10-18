using FluentAssertions;
using One4All.Core.System;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers fiscal calendars: monthly years from any month, the 4-4-5, 4-5-4, 5-4-4 and 13 x 4-week calendars, 53-week years, recognising a calendar, and the next year's code.</summary>
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
    [InlineData(FiscalCalendar.FourFiveFour, new[] { 4, 5, 4, 4, 5, 4, 4, 5, 4, 4, 5, 4 })]
    [InlineData(FiscalCalendar.FiveFourFour, new[] { 5, 4, 4, 5, 4, 4, 5, 4, 4, 5, 4, 4 })]
    [InlineData(FiscalCalendar.ThirteenFourWeek, new[] { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 })]
    public void Week_calendars_follow_their_pattern_and_are_recognised(FiscalCalendar calendar, int[] weeks)
    {
        var periods = FiscalCalendars.Periods(calendar, new DateOnly(2027, 1, 4));
        periods.Select(p => (p.End.DayNumber - p.Start.DayNumber + 1) / 7).Should().Equal(weeks);
        (periods[^1].End.DayNumber - periods[0].Start.DayNumber + 1).Should().Be(364);
        FiscalCalendars.Detect(periods).Should().Be(calendar);

        var long53 = FiscalCalendars.Periods(calendar, new DateOnly(2027, 1, 4), week53: true);
        (long53[^1].End.DayNumber - long53[0].Start.DayNumber + 1).Should().Be(371, "the 53rd week goes into the last period");
        FiscalCalendars.Detect(long53).Should().Be(calendar);
    }

    [Fact]
    public void A_53rd_week_keeps_the_year_end_near_its_anchor()
    {
        FiscalCalendars.NeedsWeek53(new DateOnly(2026, 12, 28), 12, 27).Should().BeFalse("52 weeks end the day before the anchor");
        FiscalCalendars.NeedsWeek53(new DateOnly(2026, 12, 28), 1, 3).Should().BeTrue("52 weeks would end eight days early");
        FiscalCalendars.NeedsWeek53(new DateOnly(2027, 1, 4), 1, 2).Should().BeFalse();
        FiscalCalendars.NeedsWeek53(new DateOnly(2026, 3, 2), 2, 29).Should().BeFalse("a 29 February anchor falls back to the 28th in other years");
    }

    [Theory]
    [InlineData("2026", "2027")]
    [InlineData("2026/27", "2027/28")]
    [InlineData("FY26", "FY27")]
    [InlineData("FY99", "FY00")]
    [InlineData("Year", "Year2027")]
    public void The_next_code_moves_every_number_on(string code, string next) => FiscalCalendars.NextCode(code, new DateOnly(2027, 7, 1)).Should().Be(next);
}
