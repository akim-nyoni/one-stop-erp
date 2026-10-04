using FluentAssertions;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class TariffSeasonTests
{
    private static Tariff Seasonal()
    {
        Tariff t = new(1, "WAT", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        t.AddSeason(10, 3, 1.5m); // dry season October–March wraps the year end
        return t;
    }

    [Theory]
    [InlineData(1, 1.5)]   // January, in the wrapped dry season
    [InlineData(3, 1.5)]   // March, last dry month
    [InlineData(4, 1.0)]   // April, standard rate
    [InlineData(9, 1.0)]   // September, standard rate
    [InlineData(10, 1.5)]  // October, first dry month
    public void The_per_unit_rate_follows_the_season(int month, double rate)
    {
        Seasonal().PerUnitRateFor(month).Should().Be((decimal)rate);
    }

    [Fact]
    public void A_seasonal_tariff_bills_consumption_at_the_months_rate()
    {
        Tariff t = Seasonal();
        t.CalculateCharge(100m, 0m, 0m, billMonth: 1).Should().Be(150m);   // dry season
        t.CalculateCharge(100m, 0m, 0m, billMonth: 6).Should().Be(100m);   // standard
        t.CalculateCharge(100m, 0m).Should().Be(100m);                     // no month → standard rate
    }

    [Fact]
    public void A_non_wrapping_season_covers_only_its_months()
    {
        Tariff t = new(1, "WAT", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        t.AddSeason(6, 8, 2m); // winter June–August
        t.PerUnitRateFor(7).Should().Be(2m);
        t.PerUnitRateFor(5).Should().Be(1m);
        t.PerUnitRateFor(9).Should().Be(1m);
    }

    [Fact]
    public void A_month_outside_one_to_twelve_is_rejected()
    {
        Tariff t = Seasonal();
        Action act = () => t.AddSeason(0, 3, 1.5m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
