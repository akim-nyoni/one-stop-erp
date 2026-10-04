using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class BookingCalendarTests
{
    private static DateOnly D(int day) => new(2026, 5, day);

    [Theory]
    [InlineData(10, 12, 12, 14, true)]   // share the 12th
    [InlineData(10, 12, 13, 14, false)]  // back to back
    [InlineData(10, 20, 12, 14, true)]   // one inside the other
    [InlineData(13, 14, 10, 12, false)]  // after
    public void Ranges_overlap_when_they_share_a_day(int aFrom, int aTo, int bFrom, int bTo, bool overlaps)
    {
        BookingCalendar.Overlaps(D(aFrom), D(aTo), D(bFrom), D(bTo)).Should().Be(overlaps);
    }

    [Fact]
    public void A_range_counts_both_ends()
    {
        BookingCalendar.Days(D(10), D(12)).Should().Be(3);
        BookingCalendar.Days(D(10), D(10)).Should().Be(1);
        BookingCalendar.Days(D(12), D(10)).Should().Be(0);
    }

    [Fact]
    public void The_fee_is_days_times_the_daily_rate()
    {
        BookingCalendar.Fee(D(10), D(12), 200m).Should().Be(600m);
    }

    [Theory]
    [InlineData(50, 300)]
    [InlineData(0, 0)]
    [InlineData(150, 600)]   // clamped to 100%
    public void The_refund_is_a_share_of_the_fee(int percent, int refund)
    {
        BookingCalendar.Refund(600m, percent).Should().Be(refund);
    }
}
