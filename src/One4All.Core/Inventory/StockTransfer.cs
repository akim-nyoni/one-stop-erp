using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>Where an inter-warehouse transfer stands.</summary>
public enum StockTransferStatus
{
    /// <summary>Dispatched; the goods sit in the transit location.</summary>
    InTransit = 1,

    /// <summary>Received in full.</summary>
    Received = 2,

    /// <summary>Received with a shortfall written off.</summary>
    ReceivedShort = 3,
}

/// <summary>
/// A two-step inter-warehouse transfer (<c>inv.stock_transfer</c>): dispatch moves the goods from the source into a transit
/// location; receipt moves what arrived on to the destination and writes off any shortfall.
/// </summary>
public sealed class StockTransfer : AuditableEntity, ICompanyScoped
{
    private StockTransfer()
    {
        TransferNumber = string.Empty;
    }

    /// <summary>Dispatches a transfer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="transferNumber">The transfer note number.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="fromWarehouseId">The source.</param>
    /// <param name="transitWarehouseId">The transit location.</param>
    /// <param name="toWarehouseId">The destination.</param>
    /// <param name="dispatchedOn">The dispatch date.</param>
    /// <param name="quantity">The quantity dispatched.</param>
    /// <param name="value">The cost value dispatched.</param>
    public StockTransfer(long companyId, string transferNumber, long itemId, long fromWarehouseId, long transitWarehouseId, long toWarehouseId, DateOnly dispatchedOn, decimal quantity, decimal value)
    {
        CompanyId = companyId;
        TransferNumber = transferNumber;
        ItemId = itemId;
        FromWarehouseId = fromWarehouseId;
        TransitWarehouseId = transitWarehouseId;
        ToWarehouseId = toWarehouseId;
        DispatchedOn = dispatchedOn;
        Quantity = quantity;
        Value = value;
        Status = StockTransferStatus.InTransit;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the transfer note number.</summary>
    public string TransferNumber { get; private set; }

    /// <summary>Gets the item.</summary>
    public long ItemId { get; private set; }

    /// <summary>Gets the source warehouse.</summary>
    public long FromWarehouseId { get; private set; }

    /// <summary>Gets the transit location.</summary>
    public long TransitWarehouseId { get; private set; }

    /// <summary>Gets the destination warehouse.</summary>
    public long ToWarehouseId { get; private set; }

    /// <summary>Gets the dispatch date.</summary>
    public DateOnly DispatchedOn { get; private set; }

    /// <summary>Gets the quantity dispatched.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the cost value dispatched.</summary>
    public decimal Value { get; private set; }

    /// <summary>Gets the receipt date.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>Gets the quantity that arrived.</summary>
    public decimal? ReceivedQuantity { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public StockTransferStatus Status { get; private set; }

    /// <summary>Records the receipt.</summary>
    /// <param name="date">The receipt date.</param>
    /// <param name="quantity">The quantity that arrived.</param>
    public void Receive(DateOnly date, decimal quantity)
    {
        ReceivedOn = date;
        ReceivedQuantity = quantity;
        Status = quantity < Quantity ? StockTransferStatus.ReceivedShort : StockTransferStatus.Received;
    }
}
