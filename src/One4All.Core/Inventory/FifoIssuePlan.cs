namespace One4All.Core.Inventory;

/// <summary>One layer's part in a FIFO issue.</summary>
/// <param name="Layer">The layer.</param>
/// <param name="Quantity">The quantity taken from it.</param>
/// <param name="Cost">The cost taken from it.</param>
public readonly record struct FifoTake(StockCostLayer Layer, decimal Quantity, decimal Cost);

/// <summary>
/// How a FIFO issue is costed: the oldest layers first, then any quantity beyond the layers (an issue allowed to take
/// stock negative) at a fallback unit cost. Planning changes nothing; <see cref="Apply"/> takes the stock from the layers.
/// </summary>
public sealed class FifoIssuePlan
{
    private FifoIssuePlan(IReadOnlyList<FifoTake> takes, decimal shortfall, decimal shortfallCost)
    {
        Takes = takes;
        Shortfall = shortfall;
        ShortfallCost = shortfallCost;
    }

    /// <summary>The layers drawn on, oldest first.</summary>
    public IReadOnlyList<FifoTake> Takes { get; }

    /// <summary>The quantity the layers could not cover.</summary>
    public decimal Shortfall { get; }

    /// <summary>The cost of the shortfall at the fallback unit cost.</summary>
    public decimal ShortfallCost { get; }

    /// <summary>The total cost of the issue.</summary>
    public decimal Cost => Takes.Sum(t => t.Cost) + ShortfallCost;

    /// <summary>Plans an issue against the open layers.</summary>
    /// <param name="layers">The open layers, oldest first.</param>
    /// <param name="quantity">The quantity to issue.</param>
    /// <param name="fallbackUnitCost">The unit cost for any quantity beyond the layers.</param>
    /// <returns>The plan.</returns>
    public static FifoIssuePlan For(IEnumerable<StockCostLayer> layers, decimal quantity, decimal fallbackUnitCost)
    {
        ArgumentNullException.ThrowIfNull(layers);
        List<FifoTake> takes = [];
        decimal left = quantity;
        foreach (StockCostLayer layer in layers)
        {
            if (left <= 0m)
            {
                break;
            }

            if (layer.RemainingQuantity <= 0m)
            {
                continue;
            }

            decimal take = Math.Min(left, layer.RemainingQuantity);
            takes.Add(new FifoTake(layer, take, layer.CostOf(take)));
            left -= take;
        }

        decimal shortfall = Math.Max(0m, left);
        return new FifoIssuePlan(takes, shortfall, decimal.Round(shortfall * fallbackUnitCost, 4, MidpointRounding.ToEven));
    }

    /// <summary>Takes the planned quantities from the layers.</summary>
    public void Apply()
    {
        foreach (FifoTake take in Takes)
        {
            take.Layer.Take(take.Quantity);
        }
    }
}
