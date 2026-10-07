using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>
/// A goods-received note (brief §4.3, <c>pop.grn</c>): the receipt of goods against a purchase order. Posting
/// a GRN accrues the goods-received-not-invoiced (GRNI) liability (Dr stock, Cr GRNI); the later supplier
/// invoice clears GRNI to accounts payable in the three-way match.
/// </summary>
public sealed class GoodsReceivedNote : AuditableEntity, ICompanyScoped
{
    private readonly List<GrnLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GoodsReceivedNote() => GrnNumber = string.Empty;

    /// <summary>Creates a GRN.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="purchaseOrderId">The purchase order.</param>
    /// <param name="grnNumber">The GRN number.</param>
    /// <param name="date">The receipt date.</param>
    public GoodsReceivedNote(long companyId, long supplierId, long purchaseOrderId, string grnNumber, DateOnly date)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        PurchaseOrderId = purchaseOrderId;
        GrnNumber = grnNumber;
        Date = date;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The purchase order.</summary>
    public long PurchaseOrderId { get; private set; }

    /// <summary>The GRN number.</summary>
    public string GrnNumber { get; private set; }

    /// <summary>The receipt date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The GRN lines.</summary>
    public IReadOnlyList<GrnLine> Lines => _lines.AsReadOnly();

    /// <summary>The supervisor who authorised receiving more than the order allows, if anyone did.</summary>
    public long? OverReceiptAuthorisedByUserId { get; set; }

    /// <summary>Adds a received line.</summary>
    /// <param name="purchaseOrderLineId">The PO line received against.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity received.</param>
    /// <param name="unitPrice">The PO unit price used for the accrual.</param>
    /// <returns>The created line.</returns>
    public GrnLine AddLine(long purchaseOrderLineId, long itemId, decimal quantity, decimal unitPrice)
    {
        GrnLine line = new(purchaseOrderLineId, itemId, quantity, unitPrice);
        _lines.Add(line);
        return line;
    }
}

/// <summary>A line of a <see cref="GoodsReceivedNote"/> (<c>pop.grn_line</c>).</summary>
public sealed class GrnLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GrnLine()
    {
    }

    /// <summary>Creates a GRN line.</summary>
    /// <param name="purchaseOrderLineId">The PO line.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity received.</param>
    /// <param name="unitPrice">The PO unit price.</param>
    public GrnLine(long purchaseOrderLineId, long itemId, decimal quantity, decimal unitPrice)
    {
        PurchaseOrderLineId = purchaseOrderLineId;
        ItemId = itemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The owning GRN id.</summary>
    public long GoodsReceivedNoteId { get; private set; }

    /// <summary>The PO line received against.</summary>
    public long PurchaseOrderLineId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity received.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The PO unit price used for the accrual.</summary>
    public decimal UnitPrice { get; private set; }
}
