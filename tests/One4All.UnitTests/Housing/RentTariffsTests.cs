using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

/// <summary>Covers picking the rent tariff: most specific first, then the latest in force.</summary>
public sealed class RentTariffsTests
{
    private static readonly DateOnly Jan = new(2026, 1, 1), Jul = new(2026, 7, 1);

    private static readonly RentTariffRate[] Rates =
    [
        new(RentalUnitType.Flat, null, null, Jan, 100m),
        new(RentalUnitType.Flat, null, null, Jul, 110m),
        new(RentalUnitType.Flat, "Mabvuku", null, Jan, 90m),
        new(RentalUnitType.Flat, null, RentIncomeCategory.Low, Jan, 70m),
        new(RentalUnitType.Flat, "Mabvuku", RentIncomeCategory.Low, Jan, 60m),
        new(RentalUnitType.House, null, null, Jul, 200m),
    ];

    [Fact]
    public void The_most_specific_tariff_in_force_wins()
    {
        RentTariffs.Rent(Rates, RentalUnitType.Flat, "Avondale", null, new DateOnly(2026, 3, 1)).Should().Be(100m);
        RentTariffs.Rent(Rates, RentalUnitType.Flat, "Avondale", null, Jul).Should().Be(110m, "escalated from July");
        RentTariffs.Rent(Rates, RentalUnitType.Flat, "mabvuku", RentIncomeCategory.High, Jul).Should().Be(90m, "the suburb rate");
        RentTariffs.Rent(Rates, RentalUnitType.Flat, "Avondale", RentIncomeCategory.Low, Jul).Should().Be(70m, "the category rate");
        RentTariffs.Rent(Rates, RentalUnitType.Flat, "Mabvuku", RentIncomeCategory.Low, Jul).Should().Be(60m, "suburb and category");
        RentTariffs.Rent(Rates, RentalUnitType.House, null, null, Jan).Should().BeNull("not in force yet");
        RentTariffs.Rent(Rates, RentalUnitType.Room, null, null, Jul).Should().BeNull();
    }
}
