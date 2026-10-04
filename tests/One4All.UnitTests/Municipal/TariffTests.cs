using FluentAssertions;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class TariffTests
{
    private static Tariff Stepped()
    {
        Tariff t = new(1, "WATER-DOM", "Domestic Water", ServiceType.Water, TariffType.Stepped, new DateOnly(2026, 1, 1), "4100") { FixedCharge = 5m };
        t.AddBlock(0m, 10m, 0.5m);   // first 10 m³ @ 0.5
        t.AddBlock(10m, 30m, 1.0m);  // next 20 m³ @ 1.0
        t.AddBlock(30m, null, 1.5m); // above 30 m³ @ 1.5
        return t;
    }

    [Theory]
    [InlineData(5, 7.5)]    // 5 + 5×0.5
    [InlineData(25, 25.0)]  // 5 + 10×0.5 + 15×1.0
    [InlineData(40, 45.0)]  // 5 + 10×0.5 + 20×1.0 + 10×1.5
    public void Stepped_tariff_bills_each_block(decimal consumption, decimal expected)
    {
        Stepped().CalculateCharge(consumption, valuation: 0m).Should().Be(expected);
    }

    [Fact]
    public void Per_unit_tariff()
    {
        Tariff t = new(1, "REFUSE", "Refuse", ServiceType.Refuse, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4200") { FixedCharge = 10m, PerUnitRate = 2m };
        t.CalculateCharge(8m, 0m).Should().Be(26m); // 10 + 8×2
    }

    [Fact]
    public void Rate_per_valuation_tariff()
    {
        Tariff t = new(1, "RATES", "Property Rates", ServiceType.Rates, TariffType.RatePerValuation, new DateOnly(2026, 1, 1), "4000") { RatePerValuationCents = 1.5m };
        t.CalculateCharge(0m, valuation: 100000m).Should().Be(1500m); // 100000 × 1.5c
    }

    [Fact]
    public void Minimum_charge_is_a_floor()
    {
        Tariff t = new(1, "FLAT", "Flat", ServiceType.Other, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 3m, MinimumCharge = 10m };
        t.CalculateCharge(0m, 0m).Should().Be(10m);
    }

    [Fact]
    public void Area_based_tariff_charges_the_availability_plus_rate_per_square_metre()
    {
        Tariff t = new(1, "ROADS", "Roads Levy", ServiceType.Other, TariffType.AreaBased, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 5m, RatePerSquareMetre = 0.02m };
        t.CalculateCharge(0m, 0m, areaSquareMetres: 800m).Should().Be(21m); // 5 + 800 × 0.02
        t.CalculateCharge(0m, 0m).Should().Be(5m); // no area → availability charge only
    }
}
