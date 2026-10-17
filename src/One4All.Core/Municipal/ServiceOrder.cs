using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Whether a service order cuts a service off or restores it.</summary>
public enum ServiceOrderKind
{
    /// <summary>Disconnect for arrears.</summary>
    Disconnect = 1,

    /// <summary>Reconnect once arrears are settled.</summary>
    Reconnect = 2,
}

/// <summary>Where a service order stands.</summary>
public enum ServiceOrderStatus
{
    /// <summary>Issued to the field team.</summary>
    Open = 0,

    /// <summary>Carried out.</summary>
    Executed = 1,

    /// <summary>Cancelled before it was carried out (the debt was paid, say).</summary>
    Cancelled = 2,
}

/// <summary>
/// A disconnection or reconnection order for a property's service (<c>mun.service_order</c>, brief §4.9 debt management):
/// raised when arrears pass the council's threshold, carried out by the field team, and - for a reconnection - charged a
/// fee. The arrears when it was raised are kept for the record.
/// </summary>
public sealed class ServiceOrder : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ServiceOrder()
    {
        OrderNumber = string.Empty;
    }

    /// <summary>Raises an order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="kind">Disconnect or reconnect.</param>
    /// <param name="raisedOn">When it was raised.</param>
    /// <param name="arrears">The overdue balance when it was raised.</param>
    public ServiceOrder(long companyId, string orderNumber, long propertyId, ServiceType serviceType, ServiceOrderKind kind, DateOnly raisedOn, decimal arrears)
    {
        CompanyId = companyId;
        OrderNumber = orderNumber;
        PropertyId = propertyId;
        ServiceType = serviceType;
        Kind = kind;
        RaisedOn = raisedOn;
        Arrears = arrears;
        Status = ServiceOrderStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The order number.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The service.</summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>Disconnect or reconnect.</summary>
    public ServiceOrderKind Kind { get; private set; }

    /// <summary>When it was raised.</summary>
    public DateOnly RaisedOn { get; private set; }

    /// <summary>The overdue balance when it was raised.</summary>
    public decimal Arrears { get; private set; }

    /// <summary>Where it stands.</summary>
    public ServiceOrderStatus Status { get; private set; }

    /// <summary>When it was carried out.</summary>
    public DateOnly? ExecutedOn { get; private set; }

    /// <summary>The fee invoice, for a reconnection.</summary>
    public string? FeeInvoiceNumber { get; private set; }

    /// <summary>Records the order as carried out.</summary>
    /// <param name="date">When.</param>
    /// <param name="feeInvoiceNumber">The fee charged, if any.</param>
    public void Execute(DateOnly date, string? feeInvoiceNumber)
    {
        ExecutedOn = date;
        FeeInvoiceNumber = feeInvoiceNumber;
        Status = ServiceOrderStatus.Executed;
    }

    /// <summary>Cancels an open order.</summary>
    public void Cancel() => Status = ServiceOrderStatus.Cancelled;
}
