using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>A sales order as it stood before an amendment (<c>sop.sales_order_revision</c>, brief §4.13): its lines, value, why and by whom it changed.</summary>
public sealed class SalesOrderRevision : AuditableEntity, ICompanyScoped
{
    private SalesOrderRevision()
    {
        LinesSnapshot = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Keeps the version.</summary>
    public SalesOrderRevision(long companyId, long salesOrderId, int version, decimal totalNet, string linesSnapshot, string reason, long amendedByUserId, DateOnly amendedOn)
    {
        CompanyId = companyId;
        SalesOrderId = salesOrderId;
        Version = version;
        TotalNet = totalNet;
        LinesSnapshot = linesSnapshot;
        Reason = reason;
        AmendedByUserId = amendedByUserId;
        AmendedOn = amendedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The order.</summary>
    public long SalesOrderId { get; private set; }

    /// <summary>The version this was.</summary>
    public int Version { get; private set; }

    /// <summary>Its value net of tax.</summary>
    public decimal TotalNet { get; private set; }

    /// <summary>Its lines (JSON: line, quantity, price).</summary>
    public string LinesSnapshot { get; private set; }

    /// <summary>Why it was amended.</summary>
    public string Reason { get; private set; }

    /// <summary>Who amended it.</summary>
    public long AmendedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly AmendedOn { get; private set; }
}
