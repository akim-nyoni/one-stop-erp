using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>What an item alias is.</summary>
public enum ItemAliasKind
{
    /// <summary>Another barcode for the item (pack, old label, supplier's barcode).</summary>
    Barcode = 1,

    /// <summary>A name people search by (synonym, local name, misspelling).</summary>
    Synonym = 2,
}

/// <summary>Another barcode or name an item is found by (<c>inv.item_alias</c>, brief §4.2).</summary>
public sealed class ItemAlias : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ItemAlias() => Alias = string.Empty;

    /// <summary>Creates the alias.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="kind">Barcode or synonym.</param>
    /// <param name="alias">The barcode or name.</param>
    public ItemAlias(long companyId, long itemId, ItemAliasKind kind, string alias)
    {
        CompanyId = companyId;
        ItemId = itemId;
        Kind = kind;
        Alias = alias;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>Barcode or synonym.</summary>
    public ItemAliasKind Kind { get; private set; }

    /// <summary>The barcode or name.</summary>
    public string Alias { get; private set; }
}

/// <summary>
/// A unit an item is also bought or sold in (<c>inv.item_unit</c>): a box of 12, a case of 24 — with its own barcode — converting to the
/// item's base unit by a factor.
/// </summary>
public sealed class ItemUnit : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ItemUnit() => UnitCode = string.Empty;

    /// <summary>Creates the unit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="unitCode">The unit (BOX, CASE…).</param>
    /// <param name="factor">How many base units it holds.</param>
    /// <param name="barcode">Its own barcode, if any.</param>
    public ItemUnit(long companyId, long itemId, string unitCode, decimal factor, string? barcode)
    {
        CompanyId = companyId;
        ItemId = itemId;
        UnitCode = unitCode;
        Factor = factor;
        Barcode = barcode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The unit.</summary>
    public string UnitCode { get; private set; }

    /// <summary>How many base units it holds.</summary>
    public decimal Factor { get; private set; }

    /// <summary>Its own barcode, if any.</summary>
    public string? Barcode { get; private set; }
}

/// <summary>
/// A supplier an item is bought from (<c>inv.item_supplier</c>): the supplier's own code for it, lead time, minimum order, the last price
/// paid and whether it is the preferred source.
/// </summary>
public sealed class ItemSupplier : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ItemSupplier()
    {
    }

    /// <summary>Creates the link.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="supplierId">The supplier.</param>
    public ItemSupplier(long companyId, long itemId, long supplierId)
    {
        CompanyId = companyId;
        ItemId = itemId;
        SupplierId = supplierId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>Gets or sets the supplier's own code for the item.</summary>
    public string? SupplierItemCode { get; set; }

    /// <summary>Gets or sets the lead time in days.</summary>
    public int LeadTimeDays { get; set; }

    /// <summary>Gets or sets the minimum order quantity.</summary>
    public decimal MinimumOrderQuantity { get; set; }

    /// <summary>Gets or sets the last price paid.</summary>
    public decimal? LastCost { get; set; }

    /// <summary>Gets or sets a value indicating whether this is the preferred supplier.</summary>
    public bool IsPreferred { get; set; }
}
