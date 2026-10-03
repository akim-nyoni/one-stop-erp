using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class ProRataTests
{
    private static readonly DateOnly April = new(2026, 4, 15);

    [Fact]
    public void A_service_in_place_all_month_bills_in_full()
    {
        ProRata.Fraction(April, new DateOnly(2026, 1, 1), null).Should().Be(1m);
        ProRata.Fraction(April, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)).Should().Be(1m);
    }

    [Fact]
    public void A_mid_month_connection_bills_from_its_start()
    {
        ProRata.Apply(30m, ProRata.Fraction(April, new DateOnly(2026, 4, 11), null)).Should().Be(20m);
    }

    [Fact]
    public void A_mid_month_end_bills_to_its_last_day()
    {
        ProRata.Apply(30m, ProRata.Fraction(April, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 15))).Should().Be(15m);
    }

    [Theory]
    [InlineData(2026, 5, 1, null)]
    [InlineData(2026, 1, 1, "2026-03-31")]
    public void A_service_not_in_place_that_month_bills_nothing(int y, int m, int d, string? end)
    {
        ProRata.Fraction(April, new DateOnly(y, m, d), end is null ? null : DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(0m);
    }

    [Fact]
    public void Part_months_round_to_the_cent()
    {
        ProRata.Apply(10m, ProRata.Fraction(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 19), null)).Should().Be(3.57m);
        ProRata.Month(new DateOnly(2028, 2, 10)).Should().Be((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)));
    }
}
