using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>
/// A retained earlier version of a purchase order (<c>pop.purchase_order_revision</c>, brief §4.3): the order as it
/// stood before an amendment - its lines and total - with why and by whom it was changed, so the amendment history is
/// never lost.
/// </summary>
public sealed class PurchaseOrderRevision : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PurchaseOrderRevision()
    {
        LinesSnapshot = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Retains a version.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="version">The version retained (the one being replaced).</param>
    /// <param name="totalNet">Its total.</param>
    /// <param name="linesSnapshot">Its lines, serialised.</param>
    /// <param name="reason">Why it was amended.</param>
    /// <param name="amendedByUserId">Who amended it.</param>
    /// <param name="amendedOn">When.</param>
    public PurchaseOrderRevision(long companyId, long purchaseOrderId, int version, decimal totalNet, string linesSnapshot, string reason, long amendedByUserId, DateOnly amendedOn)
    {
        CompanyId = companyId;
        PurchaseOrderId = purchaseOrderId;
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
    public long PurchaseOrderId { get; private set; }

    /// <summary>The version retained.</summary>
    public int Version { get; private set; }

    /// <summary>Its total.</summary>
    public decimal TotalNet { get; private set; }

    /// <summary>Its lines, serialised.</summary>
    public string LinesSnapshot { get; private set; }

    /// <summary>Why it was amended.</summary>
    public string Reason { get; private set; }

    /// <summary>Who amended it.</summary>
    public long AmendedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly AmendedOn { get; private set; }
}
