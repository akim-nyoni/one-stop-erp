using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>
/// An immutable record of one stock movement (<c>inv.stock_movement</c>, brief §4.2). Provides the auditable
/// movement/transaction history behind the stock balance and reconciles to the GL stock control account.
/// </summary>
public sealed class StockMovement : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StockMovement()
    {
    }

    /// <summary>Creates a stock movement.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="date">The movement date.</param>
    /// <param name="type">The movement type.</param>
    /// <param name="quantity">The signed quantity (+ in, − out).</param>
    /// <param name="unitCost">The unit cost applied.</param>
    /// <param name="value">The signed value (+ in, − out).</param>
    /// <param name="reference">An optional reference.</param>
    public StockMovement(long companyId, long itemId, long warehouseId, DateOnly date, StockMovementType type, decimal quantity, decimal unitCost, decimal value, string? reference = null)
    {
        CompanyId = companyId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Date = date;
        Type = type;
        Quantity = quantity;
        UnitCost = unitCost;
        Value = value;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The movement date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The movement type.</summary>
    public StockMovementType Type { get; private set; }

    /// <summary>The signed quantity (+ in, − out).</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The unit cost applied.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>The signed value (+ in, − out).</summary>
    public decimal Value { get; private set; }

    /// <summary>An optional reference.</summary>
    public string? Reference { get; set; }

    /// <summary>The GL journal that posted the movement.</summary>
    public long? JournalEntryId { get; set; }

    /// <summary>True once this movement has been reversed (prevents a double reversal).</summary>
    public bool IsReversed { get; private set; }

    /// <summary>Marks the movement reversed.</summary>
    public void MarkReversed() => IsReversed = true;
}
