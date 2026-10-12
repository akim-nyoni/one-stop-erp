using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>Whose goods are held.</summary>
public enum HeldStockOwner
{
    /// <summary>A supplier's goods on consignment with us: not ours until used, then billed at the agreed price.</summary>
    Supplier = 0,

    /// <summary>A customer's goods in our keeping (for repair, storage or processing): never ours.</summary>
    Customer = 1,
}

/// <summary>What happened to held goods.</summary>
public enum HeldStockMovementKind
{
    /// <summary>Taken in.</summary>
    In = 0,

    /// <summary>Handed back to the owner.</summary>
    Returned = 1,

    /// <summary>Used or sold by us (supplier-owned only): bought from the supplier and taken into our own stock.</summary>
    Used = 2,
}

/// <summary>
/// Goods we hold that belong to a supplier or a customer (<c>inv.held_stock</c>; brief §4.6 supplier-owned and customer-owned stock):
/// a quantity per owner, item and location kept outside our stock valuation. Supplier-owned goods carry the agreed price they are
/// billed at when used.
/// </summary>
public sealed class HeldStock : AuditableEntity, ICompanyScoped
{
    private readonly List<HeldStockMovement> _movements = [];

    private HeldStock()
    {
    }

    /// <summary>Opens a holding.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="owner">Supplier or customer.</param>
    /// <param name="ownerId">The supplier's or customer's id.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">Where it is held.</param>
    public HeldStock(long companyId, HeldStockOwner owner, long ownerId, long itemId, long warehouseId)
    {
        CompanyId = companyId;
        Owner = owner;
        OwnerId = ownerId;
        ItemId = itemId;
        WarehouseId = warehouseId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets whose goods these are.</summary>
    public HeldStockOwner Owner { get; private set; }

    /// <summary>Gets the supplier's or customer's id.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the item.</summary>
    public long ItemId { get; private set; }

    /// <summary>Gets where it is held.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>Gets the quantity held.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets or sets the agreed price per unit a supplier bills when its goods are used.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets the movements.</summary>
    public IReadOnlyList<HeldStockMovement> Movements => _movements;

    /// <summary>Records a movement and moves the quantity held.</summary>
    /// <param name="kind">In, returned or used.</param>
    /// <param name="quantity">The quantity (positive).</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">The document reference.</param>
    /// <param name="journalNumber">The journal for a use, if any.</param>
    public void Move(HeldStockMovementKind kind, decimal quantity, DateOnly date, string? reference, string? journalNumber = null)
    {
        Quantity += kind == HeldStockMovementKind.In ? quantity : -quantity;
        _movements.Add(new HeldStockMovement(kind, quantity, date, reference, journalNumber));
    }
}

/// <summary>A movement of held goods (<c>inv.held_stock_movement</c>).</summary>
public sealed class HeldStockMovement : Entity
{
    private HeldStockMovement()
    {
    }

    /// <summary>Records a movement.</summary>
    /// <param name="kind">In, returned or used.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="journalNumber">The journal, if any.</param>
    public HeldStockMovement(HeldStockMovementKind kind, decimal quantity, DateOnly date, string? reference, string? journalNumber)
    {
        Kind = kind;
        Quantity = quantity;
        Date = date;
        Reference = reference;
        JournalNumber = journalNumber;
    }

    /// <summary>Gets the holding.</summary>
    public long HeldStockId { get; private set; }

    /// <summary>Gets what happened.</summary>
    public HeldStockMovementKind Kind { get; private set; }

    /// <summary>Gets the quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the reference.</summary>
    public string? Reference { get; private set; }

    /// <summary>Gets the supplier invoice raised for a use, if any.</summary>
    public string? JournalNumber { get; private set; }
}
