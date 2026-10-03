namespace One4All.Application.Inventory;

/// <summary>
/// The provisioning policy for stock (IAS 2): a write-down to net realisable value, and an ageing provision for stock
/// that has not moved — the larger of the two applies to each holding, never more than its cost.
/// </summary>
/// <param name="CostToSellPercent">Selling costs as a percent of the selling price (NRV = price × (1 − this)).</param>
/// <param name="SlowMovingDays">Days without an issue after which stock counts as slow-moving.</param>
/// <param name="SlowMovingPercent">The provision on slow-moving stock, as a percent of its cost.</param>
/// <param name="ObsoleteDays">Days without an issue after which stock counts as obsolete.</param>
/// <param name="ObsoletePercent">The provision on obsolete stock, as a percent of its cost.</param>
public readonly record struct StockProvisionPolicy(decimal CostToSellPercent, int SlowMovingDays, decimal SlowMovingPercent, int ObsoleteDays, decimal ObsoletePercent)
{
    /// <summary>A common policy: 10% to sell; 25% after 180 days without an issue; 100% after 365.</summary>
    public static StockProvisionPolicy Default => new(10m, 180, 25m, 365, 100m);
}

/// <summary>One stock holding to assess.</summary>
/// <param name="Quantity">Quantity on hand at the date.</param>
/// <param name="Cost">Its carrying cost at the date.</param>
/// <param name="SellingPrice">The expected selling price per unit, when known.</param>
/// <param name="DaysSinceLastIssue">Days since the holding last had stock issued (or since it was first received).</param>
public readonly record struct StockHolding(decimal Quantity, decimal Cost, decimal? SellingPrice, int DaysSinceLastIssue);

/// <summary>Why a holding is provided for.</summary>
public enum ProvisionBasis
{
    /// <summary>No provision needed.</summary>
    None = 0,

    /// <summary>Written down to net realisable value.</summary>
    NetRealisableValue = 1,

    /// <summary>Slow-moving.</summary>
    SlowMoving = 2,

    /// <summary>Obsolete.</summary>
    Obsolete = 3,
}

/// <summary>A holding's provision.</summary>
/// <param name="NetRealisableValue">Its net realisable value, when a selling price is known.</param>
/// <param name="Provision">The provision against its cost.</param>
/// <param name="Basis">Which rule set it.</param>
public readonly record struct StockProvisionResult(decimal? NetRealisableValue, decimal Provision, ProvisionBasis Basis);

/// <summary>
/// The stock provision rule (IAS 2.9: inventories at the lower of cost and net realisable value, with slow-moving and
/// obsolete stock provided for). Pure, so it is unit-tested; the stock provision service supplies the holdings.
/// </summary>
public static class StockProvision
{
    /// <summary>Assesses a holding under a policy.</summary>
    /// <param name="holding">The holding.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The provision and its basis.</returns>
    public static StockProvisionResult Assess(StockHolding holding, StockProvisionPolicy policy)
    {
        if (holding.Quantity <= 0m || holding.Cost <= 0m)
        {
            return new StockProvisionResult(null, 0m, ProvisionBasis.None);
        }

        decimal? nrv = holding.SellingPrice is { } price
            ? Round(holding.Quantity * price * (1m - (policy.CostToSellPercent / 100m)))
            : null;
        decimal nrvProvision = nrv is { } value ? Math.Max(0m, holding.Cost - value) : 0m;

        (decimal ageingProvision, ProvisionBasis ageingBasis) =
            holding.DaysSinceLastIssue >= policy.ObsoleteDays ? (Round(holding.Cost * policy.ObsoletePercent / 100m), ProvisionBasis.Obsolete)
            : holding.DaysSinceLastIssue >= policy.SlowMovingDays ? (Round(holding.Cost * policy.SlowMovingPercent / 100m), ProvisionBasis.SlowMoving)
            : (0m, ProvisionBasis.None);

        (decimal provision, ProvisionBasis basis) = nrvProvision >= ageingProvision && nrvProvision > 0m
            ? (nrvProvision, ProvisionBasis.NetRealisableValue)
            : (ageingProvision, ageingBasis);

        return new StockProvisionResult(nrv, Math.Min(provision, holding.Cost), provision == 0m ? ProvisionBasis.None : basis);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
