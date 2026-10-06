using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>The lifecycle of a purchase requisition.</summary>
public enum PurchaseRequisitionStatus
{
    /// <summary>Being prepared.</summary>
    Draft = 1,

    /// <summary>Submitted for approval.</summary>
    Submitted = 2,

    /// <summary>Approved for ordering.</summary>
    Approved = 3,

    /// <summary>Rejected.</summary>
    Rejected = 4,

    /// <summary>Converted into a purchase order.</summary>
    Converted = 5,
}

/// <summary>
/// A purchase requisition (<c>pop.requisition</c>): an internal request to buy that precedes a purchase order.
/// It touches no ledger; once approved it can be converted into a purchase order, which then drives receiving
/// and invoice matching.
/// </summary>
public sealed class PurchaseRequisition : AuditableEntity, ICompanyScoped
{
    private readonly List<PurchaseRequisitionLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PurchaseRequisition() => RequisitionNumber = string.Empty;

    /// <summary>Creates a draft requisition.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The intended supplier.</param>
    /// <param name="requisitionNumber">The requisition number.</param>
    /// <param name="requestedDate">The date requested.</param>
    /// <param name="currency">The currency.</param>
    public PurchaseRequisition(long companyId, long supplierId, string requisitionNumber, DateOnly requestedDate, CurrencyCode currency)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        RequisitionNumber = requisitionNumber;
        RequestedDate = requestedDate;
        Currency = currency;
        Status = PurchaseRequisitionStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The intended supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The requisition number.</summary>
    public string RequisitionNumber { get; private set; }

    /// <summary>The date requested.</summary>
    public DateOnly RequestedDate { get; private set; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The status.</summary>
    public PurchaseRequisitionStatus Status { get; private set; }

    /// <summary>The approver, once approved.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>The purchase order this requisition became, once converted.</summary>
    public long? PurchaseOrderId { get; private set; }

    /// <summary>The requisition lines.</summary>
    public IReadOnlyList<PurchaseRequisitionLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets or sets the budget check's outcome at the last gate (warn / approval needed show to the approver).</summary>
    public Budgeting.BudgetAction BudgetOutcome { get; set; }

    /// <summary>The estimated total (sum of line values).</summary>
    public decimal Total => _lines.Sum(l => l.Quantity * l.UnitPrice);

    /// <summary>Adds a line.</summary>
    /// <param name="itemId">The stock item.</param>
    /// <param name="description">The line description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The estimated unit price.</param>
    public PurchaseRequisitionLine AddLine(long itemId, string description, decimal quantity, decimal unitPrice)
    {
        PurchaseRequisitionLine line = new(itemId, description, quantity, unitPrice);
        _lines.Add(line);
        return line;
    }

    /// <summary>Submits the requisition for approval.</summary>
    public void Submit() => Status = PurchaseRequisitionStatus.Submitted;

    /// <summary>Approves the requisition.</summary>
    /// <param name="approvedByUserId">The approver.</param>
    public void Approve(long approvedByUserId)
    {
        Status = PurchaseRequisitionStatus.Approved;
        ApprovedByUserId = approvedByUserId;
    }

    /// <summary>Rejects the requisition.</summary>
    public void Reject() => Status = PurchaseRequisitionStatus.Rejected;

    /// <summary>Marks the requisition converted, recording the resulting order.</summary>
    /// <param name="purchaseOrderId">The created purchase order.</param>
    public void MarkConverted(long purchaseOrderId)
    {
        Status = PurchaseRequisitionStatus.Converted;
        PurchaseOrderId = purchaseOrderId;
    }
}

/// <summary>One line of a purchase requisition (<c>pop.requisition_line</c>).</summary>
public sealed class PurchaseRequisitionLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PurchaseRequisitionLine() => Description = string.Empty;

    /// <summary>Creates a requisition line.</summary>
    /// <param name="itemId">The stock item.</param>
    /// <param name="description">The line description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The estimated unit price.</param>
    public PurchaseRequisitionLine(long itemId, string description, decimal quantity, decimal unitPrice)
    {
        ItemId = itemId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The owning requisition id.</summary>
    public long PurchaseRequisitionId { get; private set; }

    /// <summary>The stock item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The line description.</summary>
    public string Description { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The estimated unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Gets or sets the expense account the line will be charged to, for the budget check at requisition.</summary>
    public string? ExpenseAccountCode { get; set; }
}
