using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>
/// An inventory item (brief §4.2, <c>inv.item</c>). Carries the costing method and the GL accounts used
/// when its stock moves (the stock control account is resolved from the chart of accounts; cost of sales
/// is on the item so different item groups can hit different COGS accounts).
/// </summary>
public sealed class Item : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Item()
    {
        Code = string.Empty;
        Description = string.Empty;
        UnitOfMeasure = "EA";
    }

    /// <summary>Creates an item.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The item code.</param>
    /// <param name="description">The description.</param>
    /// <param name="itemType">The item type.</param>
    /// <param name="costingMethod">The costing method.</param>
    public Item(long companyId, string code, string description, ItemType itemType = ItemType.Stock, CostingMethod costingMethod = CostingMethod.WeightedAverage)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Description = description;
        ItemType = itemType;
        CostingMethod = costingMethod;
        UnitOfMeasure = "EA";
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item code.</summary>
    public string Code { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; set; }

    /// <summary>A short description for receipts.</summary>
    public string? ShortDescription { get; set; }

    /// <summary>The item type.</summary>
    public ItemType ItemType { get; private set; }

    /// <summary>The costing method.</summary>
    public CostingMethod CostingMethod { get; private set; }

    /// <summary>The stock-keeping unit of measure.</summary>
    public string UnitOfMeasure { get; set; }

    /// <summary>The default tax code.</summary>
    public string? TaxCode { get; set; }

    /// <summary>The item category.</summary>
    public string? Category { get; set; }

    /// <summary>A primary barcode.</summary>
    public string? Barcode { get; set; }

    /// <summary>The cost-of-sales account code hit when the item is issued/sold.</summary>
    public string? CostOfSalesAccountCode { get; set; }

    /// <summary>The revenue account code for sales.</summary>
    public string? RevenueAccountCode { get; set; }

    /// <summary>Reorder level (below which replenishment is suggested).</summary>
    public decimal ReorderLevel { get; set; }

    /// <summary>Reorder quantity.</summary>
    public decimal ReorderQuantity { get; set; }

    /// <summary>The standard unit cost a standard-costed item is valued at.</summary>
    public decimal? StandardCost { get; set; }

    /// <summary>
    /// Where a standard-costed item's purchase price variance goes: the difference between what it cost and its standard,
    /// on receipts and on landed charges.
    /// </summary>
    public string? PriceVarianceAccountCode { get; set; }

    /// <summary>The weight of one unit in kilograms, used to share landed costs by weight.</summary>
    public decimal? UnitWeightKg { get; set; }

    /// <summary>The volume of one unit in cubic metres, used to share landed costs by volume.</summary>
    public decimal? UnitVolumeM3 { get; set; }

    /// <summary>Gets or sets how the item's units are traced (batch or serial).</summary>
    public ItemTracking Tracking { get; set; }

    /// <summary>Gets or sets the shelf life in days, giving received batches an expiry when none is keyed.</summary>
    public int? ShelfLifeDays { get; set; }

    /// <summary>Whether the item is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Whether the item is quantity-tracked stock.</summary>
    public bool IsStockTracked => ItemType is ItemType.Stock or ItemType.Weighed;
}

/// <summary>A warehouse / stock location (brief §4.2, <c>inv.warehouse</c>).</summary>
public sealed class Warehouse : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a warehouse.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The warehouse code.</param>
    /// <param name="name">The warehouse name.</param>
    public Warehouse(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The warehouse code.</summary>
    public string Code { get; private set; }

    /// <summary>The warehouse name.</summary>
    public string Name { get; set; }

    /// <summary>
    /// Whether this is a quarantine (inspection hold) location: goods here await quality inspection and are not
    /// available to sell.
    /// </summary>
    public bool IsQuarantine { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a transit location holding goods between dispatch and receipt.</summary>
    public bool IsTransit { get; set; }

    /// <summary>Gets or sets the customer whose site this is when the warehouse holds stock on consignment (still ours until sold).</summary>
    public long? ConsignmentCustomerId { get; set; }

    /// <summary>Whether the warehouse is active.</summary>
    public bool IsActive { get; set; }
}
