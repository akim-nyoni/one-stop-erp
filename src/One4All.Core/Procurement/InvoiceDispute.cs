using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>Where a disputed supplier invoice stands.</summary>
public enum DisputeStatus
{
    /// <summary>In the queue, awaiting a decision.</summary>
    Open = 0,

    /// <summary>Accepted as invoiced and posted.</summary>
    Accepted = 1,

    /// <summary>Rejected back to the supplier for a corrected invoice.</summary>
    Rejected = 2,
}

/// <summary>
/// A supplier invoice that failed the three-way match (<c>pop.invoice_dispute</c>, brief §4.3): kept in a query queue
/// with the invoice as presented and why it failed, instead of being lost with the error, until someone accepts it
/// (posting it as invoiced) or rejects it back to the supplier.
/// </summary>
public sealed class InvoiceDispute : AuditableEntity, ICompanyScoped
{
    private readonly List<InvoiceDisputeLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InvoiceDispute()
    {
        InvoiceNumber = string.Empty;
        ReasonCode = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Queues a disputed invoice.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The purchase order it was matched against.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="invoiceNumber">The supplier's invoice number.</param>
    /// <param name="invoiceDate">The invoice date.</param>
    /// <param name="reasonCode">The match failure code.</param>
    /// <param name="reason">The match failure in words.</param>
    public InvoiceDispute(long companyId, long purchaseOrderId, long supplierId, string invoiceNumber, DateOnly invoiceDate, string reasonCode, string reason)
    {
        CompanyId = companyId;
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        InvoiceNumber = invoiceNumber;
        InvoiceDate = invoiceDate;
        ReasonCode = reasonCode;
        Reason = reason;
        Status = DisputeStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The purchase order.</summary>
    public long PurchaseOrderId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The supplier's invoice number.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>The invoice date.</summary>
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>The match failure code.</summary>
    public string ReasonCode { get; private set; }

    /// <summary>The match failure in words.</summary>
    public string Reason { get; private set; }

    /// <summary>Where it stands.</summary>
    public DisputeStatus Status { get; private set; }

    /// <summary>Who decided it.</summary>
    public long? ResolvedByUserId { get; private set; }

    /// <summary>The decision note.</summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>The AP journal, when accepted and posted.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>The invoice lines as presented.</summary>
    public IReadOnlyList<InvoiceDisputeLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds an invoiced line as presented.</summary>
    /// <param name="purchaseOrderLineId">The order line.</param>
    /// <param name="quantity">The quantity invoiced.</param>
    /// <param name="unitPrice">The price invoiced.</param>
    public void AddLine(long purchaseOrderLineId, decimal quantity, decimal unitPrice) => _lines.Add(new InvoiceDisputeLine(purchaseOrderLineId, quantity, unitPrice));

    /// <summary>Accepts the invoice as presented, now posted.</summary>
    /// <param name="userId">Who accepted it.</param>
    /// <param name="note">Why.</param>
    /// <param name="journalNumber">The posting.</param>
    public void Accept(long userId, string note, string journalNumber)
    {
        Status = DisputeStatus.Accepted;
        ResolvedByUserId = userId;
        ResolutionNote = note;
        JournalNumber = journalNumber;
    }

    /// <summary>Rejects the invoice back to the supplier.</summary>
    /// <param name="userId">Who rejected it.</param>
    /// <param name="note">Why.</param>
    public void Reject(long userId, string note)
    {
        Status = DisputeStatus.Rejected;
        ResolvedByUserId = userId;
        ResolutionNote = note;
    }
}

/// <summary>An invoiced line as presented (<c>pop.invoice_dispute_line</c>).</summary>
public sealed class InvoiceDisputeLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InvoiceDisputeLine()
    {
    }

    /// <summary>Creates a line.</summary>
    /// <param name="purchaseOrderLineId">The order line.</param>
    /// <param name="quantity">The quantity invoiced.</param>
    /// <param name="unitPrice">The price invoiced.</param>
    public InvoiceDisputeLine(long purchaseOrderLineId, decimal quantity, decimal unitPrice)
    {
        PurchaseOrderLineId = purchaseOrderLineId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The dispute.</summary>
    public long InvoiceDisputeId { get; private set; }

    /// <summary>The order line.</summary>
    public long PurchaseOrderLineId { get; private set; }

    /// <summary>The quantity invoiced.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The price invoiced.</summary>
    public decimal UnitPrice { get; private set; }
}
