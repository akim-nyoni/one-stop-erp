using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>
/// A drop-shipment (<c>sop.drop_shipment</c>, brief §4.13): sales-order lines the supplier delivers straight to the customer, so the goods
/// never pass through stock. When the supplier confirms delivery, its invoice goes to cost of sales and the order lines count as
/// despatched, ready to invoice to the customer.
/// </summary>
public sealed class DropShipment : AuditableEntity, ICompanyScoped
{
    private readonly List<DropShipmentLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DropShipment() => Reference = string.Empty;

    /// <summary>Creates the drop-shipment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The sales order.</param>
    /// <param name="supplierId">The supplier delivering.</param>
    /// <param name="reference">The reference (the order placed with the supplier).</param>
    /// <param name="orderedOn">When it was placed.</param>
    public DropShipment(long companyId, long salesOrderId, long supplierId, string reference, DateOnly orderedOn)
    {
        CompanyId = companyId;
        SalesOrderId = salesOrderId;
        SupplierId = supplierId;
        Reference = reference.Trim().ToUpperInvariant();
        OrderedOn = orderedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The sales order.</summary>
    public long SalesOrderId { get; private set; }

    /// <summary>The supplier delivering.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The reference.</summary>
    public string Reference { get; private set; }

    /// <summary>When it was placed.</summary>
    public DateOnly OrderedOn { get; private set; }

    /// <summary>When the supplier confirmed delivery to the customer.</summary>
    public DateOnly? DeliveredOn { get; private set; }

    /// <summary>The supplier's invoice.</summary>
    public string? SupplierInvoiceNumber { get; private set; }

    /// <summary>The lines.</summary>
    public IReadOnlyList<DropShipmentLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets the cost of the goods (quantity × supplier price).</summary>
    public decimal Cost => _lines.Sum(l => decimal.Round(l.Quantity * l.UnitCost, 2, MidpointRounding.AwayFromZero));

    /// <summary>Adds a line.</summary>
    /// <param name="salesOrderLineId">The order line.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitCost">The supplier's price.</param>
    public void AddLine(long salesOrderLineId, decimal quantity, decimal unitCost) => _lines.Add(new DropShipmentLine(salesOrderLineId, quantity, unitCost));

    /// <summary>Records the supplier's confirmation of delivery.</summary>
    /// <param name="date">The delivery date.</param>
    /// <param name="supplierInvoiceNumber">The supplier's invoice.</param>
    public void Delivered(DateOnly date, string supplierInvoiceNumber)
    {
        DeliveredOn = date;
        SupplierInvoiceNumber = supplierInvoiceNumber;
    }
}

/// <summary>One sales-order line on a drop-shipment.</summary>
public sealed class DropShipmentLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DropShipmentLine()
    {
    }

    /// <summary>Creates the line.</summary>
    /// <param name="salesOrderLineId">The order line.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitCost">The supplier's price.</param>
    public DropShipmentLine(long salesOrderLineId, decimal quantity, decimal unitCost)
    {
        SalesOrderLineId = salesOrderLineId;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    /// <summary>The owning drop-shipment.</summary>
    public long DropShipmentId { get; private set; }

    /// <summary>The order line.</summary>
    public long SalesOrderLineId { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The supplier's price.</summary>
    public decimal UnitCost { get; private set; }
}
