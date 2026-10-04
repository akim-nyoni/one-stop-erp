using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>Development levy amounts (brief §4.10.2): flat, per square metre of the stand, or a percentage of the price. Pure and unit-tested.</summary>
public static class LevyCalculator
{
    /// <summary>The levy on a stand.</summary>
    /// <param name="basis">How it is measured.</param>
    /// <param name="rate">The amount, rate per m² or percentage.</param>
    /// <param name="sizeSquareMetres">The stand's size.</param>
    /// <param name="price">The price or consideration, when there is one.</param>
    /// <returns>The amount, or null when a percentage levy has no price to apply to.</returns>
    public static decimal? Amount(LevyBasis basis, decimal rate, decimal sizeSquareMetres, decimal? price) => basis switch
    {
        LevyBasis.Flat => rate,
        LevyBasis.PerSquareMetre => Math.Round(rate * sizeSquareMetres, 2, MidpointRounding.AwayFromZero),
        LevyBasis.PercentOfPrice => price is decimal p ? Math.Round(p * rate / 100m, 2, MidpointRounding.AwayFromZero) : null,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown levy basis."),
    };
}
