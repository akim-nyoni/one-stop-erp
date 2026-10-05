using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>
/// A delivery note raised when goods are despatched against a sales order (<c>sop.delivery</c>): what went, on which vehicle and route,
/// and — once signed for — when it arrived and who received it (proof of delivery).
/// </summary>
public sealed class SalesDelivery : AuditableEntity, ICompanyScoped
{
    private readonly List<SalesDeliveryLine> _lines = [];

    private SalesDelivery()
    {
        Reference = string.Empty;
    }

    /// <summary>Raises a delivery note.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The order.</param>
    /// <param name="reference">The despatch reference.</param>
    /// <param name="despatchedOn">The despatch date.</param>
    public SalesDelivery(long companyId, long salesOrderId, string reference, DateOnly despatchedOn)
    {
        CompanyId = companyId;
        SalesOrderId = salesOrderId;
        Reference = reference;
        DespatchedOn = despatchedOn;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the order.</summary>
    public long SalesOrderId { get; private set; }

    /// <summary>Gets the despatch reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets the despatch date.</summary>
    public DateOnly DespatchedOn { get; private set; }

    /// <summary>Gets or sets the date the delivery is planned for.</summary>
    public DateOnly? PlannedFor { get; set; }

    /// <summary>Gets or sets the vehicle.</summary>
    public string? Vehicle { get; set; }

    /// <summary>Gets or sets the route.</summary>
    public string? Route { get; set; }

    /// <summary>Gets or sets the driver.</summary>
    public string? Driver { get; set; }

    /// <summary>Gets when it was delivered (proof of delivery).</summary>
    public DateOnly? DeliveredOn { get; private set; }

    /// <summary>Gets who signed for it.</summary>
    public string? ReceivedBy { get; private set; }

    /// <summary>Gets the signed POD reference.</summary>
    public string? PodReference { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<SalesDeliveryLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a line.</summary>
    /// <param name="salesOrderLineId">The order line.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity sent.</param>
    public void AddLine(long salesOrderLineId, long itemId, decimal quantity) => _lines.Add(new SalesDeliveryLine(salesOrderLineId, itemId, quantity));

    /// <summary>Records proof of delivery.</summary>
    /// <param name="deliveredOn">When it arrived.</param>
    /// <param name="receivedBy">Who signed.</param>
    /// <param name="podReference">The POD reference.</param>
    public void ConfirmDelivery(DateOnly deliveredOn, string receivedBy, string? podReference)
    {
        DeliveredOn = deliveredOn;
        ReceivedBy = receivedBy;
        PodReference = podReference;
    }
}

/// <summary>One line on a delivery note.</summary>
public sealed class SalesDeliveryLine : Entity
{
    private SalesDeliveryLine()
    {
    }

    /// <summary>Creates a line.</summary>
    /// <param name="salesOrderLineId">The order line.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    public SalesDeliveryLine(long salesOrderLineId, long itemId, decimal quantity)
    {
        SalesOrderLineId = salesOrderLineId;
        ItemId = itemId;
        Quantity = quantity;
    }

    /// <summary>Gets the delivery.</summary>
    public long SalesDeliveryId { get; private set; }

    /// <summary>Gets the order line.</summary>
    public long SalesOrderLineId { get; private set; }

    /// <summary>Gets the item.</summary>
    public long ItemId { get; private set; }

    /// <summary>Gets the quantity.</summary>
    public decimal Quantity { get; private set; }
}
