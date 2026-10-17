using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>
/// The density a stand serves, by size (brief §14 H29 demand vs supply, H96 statutory return): the usual planning bands -
/// up to 300 m² is high density, up to 800 m² medium, larger low. Rental units supply the flats and rental categories
/// instead. Pure and unit-tested.
/// </summary>
public static class HousingDensity
{
    /// <summary>The largest high-density stand, in m².</summary>
    public const decimal HighDensityMaximum = 300m;

    /// <summary>The largest medium-density stand, in m².</summary>
    public const decimal MediumDensityMaximum = 800m;

    /// <summary>The category a stand of a size supplies.</summary>
    /// <param name="sizeSquareMetres">The stand's size.</param>
    /// <returns>High, medium or low density.</returns>
    public static HousingCategory ForStand(decimal sizeSquareMetres) => sizeSquareMetres switch
    {
        <= HighDensityMaximum => HousingCategory.HighDensity,
        <= MediumDensityMaximum => HousingCategory.MediumDensity,
        _ => HousingCategory.LowDensity,
    };

    /// <summary>The category a rental unit supplies.</summary>
    /// <param name="type">The unit's type.</param>
    /// <returns>Flats for flats, rental otherwise.</returns>
    public static HousingCategory ForUnit(RentalUnitType type) => type == RentalUnitType.Flat ? HousingCategory.Flats : HousingCategory.Rental;
}
