namespace One4All.Core.Inventory;

/// <summary>The nature of an inventory item (brief §4.2).</summary>
public enum ItemType
{
    /// <summary>A stocked, quantity-tracked item.</summary>
    Stock = 1,

    /// <summary>A non-stock item (bought/sold but not tracked).</summary>
    NonStock = 2,

    /// <summary>A service.</summary>
    Service = 3,

    /// <summary>A kit / bill of materials.</summary>
    Kit = 4,

    /// <summary>A weighed / catch-weight item.</summary>
    Weighed = 5,
}

/// <summary>The inventory costing method (brief §3.6, IAS 2).</summary>
public enum CostingMethod
{
    /// <summary>Moving weighted average.</summary>
    WeightedAverage = 0,

    /// <summary>First in, first out (cost layers).</summary>
    Fifo = 1,

    /// <summary>Standard cost with variance to a separate account.</summary>
    Standard = 2,

    /// <summary>Specific identification (serial/batch).</summary>
    SpecificIdentification = 3,
}

/// <summary>The kind of stock movement (brief §4.2).</summary>
public enum StockMovementType
{
    /// <summary>Goods received / receipt (quantity in).</summary>
    Receipt = 1,

    /// <summary>Issue / sale (quantity out).</summary>
    Issue = 2,

    /// <summary>Transfer between warehouses.</summary>
    Transfer = 3,

    /// <summary>Adjustment (count/write-off), signed.</summary>
    Adjustment = 4,

    /// <summary>Reversal of a prior movement (puts an issued quantity/value back at its original cost).</summary>
    Reversal = 5,

    /// <summary>Return of goods to a supplier (quantity out at moving-average cost).</summary>
    SupplierReturn = 6,
}
