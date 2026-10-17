using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>A rent tariff line.</summary>
/// <param name="UnitType">The kind of unit.</param>
/// <param name="Suburb">Only in this suburb, or null for any.</param>
/// <param name="IncomeCategory">Only for this income category, or null for any.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="MonthlyRent">The monthly rent.</param>
public sealed record RentTariffRate(RentalUnitType UnitType, string? Suburb, RentIncomeCategory? IncomeCategory, DateOnly EffectiveFrom, decimal MonthlyRent);

/// <summary>
/// Picks the rent a tariff sets (brief §4.10.7 rent tariffs by unit type, suburb and income category): among the tariffs for the kind of unit in force
/// on the date, the most specific wins - suburb and category, then suburb, then category, then the general rate - and of those the latest. Pure.
/// </summary>
public static class RentTariffs
{
    /// <summary>The rent for a unit and household on a date.</summary>
    /// <param name="rates">The tariffs.</param>
    /// <param name="unitType">The kind of unit.</param>
    /// <param name="suburb">The unit's suburb, or null.</param>
    /// <param name="category">The household's income category, or null (only tariffs for any category apply).</param>
    /// <param name="on">The date.</param>
    /// <returns>The rent, or null when no tariff covers it.</returns>
    public static decimal? Rent(IEnumerable<RentTariffRate> rates, RentalUnitType unitType, string? suburb, RentIncomeCategory? category, DateOnly on)
        => rates.Where(r => r.UnitType == unitType && r.EffectiveFrom <= on
                && (r.Suburb is null || string.Equals(r.Suburb, suburb, StringComparison.OrdinalIgnoreCase))
                && (r.IncomeCategory is null || r.IncomeCategory == category))
            .OrderByDescending(r => (r.Suburb is null ? 0 : 2) + (r.IncomeCategory is null ? 0 : 1)).ThenByDescending(r => r.EffectiveFrom)
            .Select(r => (decimal?)r.MonthlyRent).FirstOrDefault();
}
