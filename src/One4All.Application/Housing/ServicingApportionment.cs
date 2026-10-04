using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>A stand's measures for apportioning servicing cost.</summary>
/// <param name="StandId">The stand.</param>
/// <param name="AreaSquareMetres">Its size.</param>
/// <param name="FrontageMetres">Its road frontage, if surveyed.</param>
/// <param name="ZoningWeight">Its zoning weight, if set.</param>
public sealed record StandMeasures(long StandId, decimal AreaSquareMetres, decimal? FrontageMetres, decimal? ZoningWeight);

/// <summary>A stand's share of servicing cost.</summary>
/// <param name="StandId">The stand.</param>
/// <param name="Amount">Its share.</param>
public sealed record StandShare(long StandId, decimal Amount);

/// <summary>
/// Apportionment of servicing cost to stands (brief §4.10.2): by area, by frontage, by equal share or by zoning weighting,
/// to the cent, the rounding on the stand with the largest basis so the shares equal the cost. Pure and unit-tested.
/// </summary>
public static class ServicingApportionment
{
    /// <summary>Apportions an amount.</summary>
    /// <param name="stands">The stands.</param>
    /// <param name="basis">The basis.</param>
    /// <param name="amount">The cost to apportion.</param>
    /// <param name="shares">Each stand's share, in the order given.</param>
    /// <returns>What is wrong, or null on success.</returns>
    public static string? Apportion(IReadOnlyList<StandMeasures> stands, ServicingBasis basis, decimal amount, out IReadOnlyList<StandShare> shares)
    {
        ArgumentNullException.ThrowIfNull(stands);
        shares = [];
        if (stands.Count == 0)
        {
            return "There are no stands to apportion to.";
        }

        List<decimal> weights = [];
        foreach (StandMeasures stand in stands)
        {
            decimal? weight = basis switch
            {
                ServicingBasis.Equal => 1m,
                ServicingBasis.ByArea => stand.AreaSquareMetres,
                ServicingBasis.ByFrontage => stand.FrontageMetres,
                ServicingBasis.ByZoningWeight => stand.ZoningWeight,
                _ => null,
            };
            if (weight is not > 0m)
            {
                return $"Stand {stand.StandId} has no positive {basis} measure.";
            }

            weights.Add(weight.Value);
        }

        decimal total = weights.Sum();
        List<StandShare> result = [.. stands.Select((s, i) => new StandShare(s.StandId, Math.Round(amount * weights[i] / total, 2, MidpointRounding.AwayFromZero)))];
        int largest = weights.IndexOf(weights.Max());
        result[largest] = result[largest] with { Amount = result[largest].Amount + (amount - result.Sum(r => r.Amount)) };
        shares = result;
        return null;
    }
}
