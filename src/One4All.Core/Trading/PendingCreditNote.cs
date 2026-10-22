using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a credit note held for approval stands.</summary>
public enum PendingCreditNoteStatus
{
    /// <summary>In its approval workflow.</summary>
    Pending = 1,

    /// <summary>Approved and posted.</summary>
    Posted = 2,

    /// <summary>Rejected or returned.</summary>
    Rejected = 3,
}

/// <summary>
/// A customer credit note held for approval (<c>ar.pending_credit_note</c>, brief §7): what will post once its workflow approves it - nothing
/// reaches the ledger before then.
/// </summary>
public sealed class PendingCreditNote : AuditableEntity, ICompanyScoped
{
    private PendingCreditNote()
    {
        DocumentNumber = string.Empty;
        Currency = string.Empty;
        SalesAccountCode = string.Empty;
    }

    /// <summary>Holds the credit note.</summary>
    public PendingCreditNote(long companyId, long customerId, string documentNumber, DateOnly documentDate, string currency, decimal netAmount, decimal taxAmount, string salesAccountCode,
        string? taxAccountCode, string? narrative, string? reasonCode, long submittedByUserId)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        DocumentNumber = documentNumber;
        DocumentDate = documentDate;
        Currency = currency;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
        SalesAccountCode = salesAccountCode;
        TaxAccountCode = taxAccountCode;
        Narrative = narrative;
        ReasonCode = reasonCode;
        SubmittedByUserId = submittedByUserId;
        Status = PendingCreditNoteStatus.Pending;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The credit note number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>Its date.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>Its currency.</summary>
    public string Currency { get; private set; }

    /// <summary>The net amount.</summary>
    public decimal NetAmount { get; private set; }

    /// <summary>The tax.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>The revenue account debited.</summary>
    public string SalesAccountCode { get; private set; }

    /// <summary>The tax account.</summary>
    public string? TaxAccountCode { get; private set; }

    /// <summary>The tax code the credit's tax is tagged with (and worked out from, when no tax was keyed).</summary>
    public string? TaxCode { get; set; }

    /// <summary>The invoice the credit note reverses, when one.</summary>
    public string? OriginalInvoiceNumber { get; set; }

    /// <summary>The narrative.</summary>
    public string? Narrative { get; private set; }

    /// <summary>The reason code.</summary>
    public string? ReasonCode { get; private set; }

    /// <summary>Who raised it.</summary>
    public long SubmittedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public PendingCreditNoteStatus Status { get; private set; }

    /// <summary>The posting journal, once posted.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>Marks it posted.</summary>
    /// <param name="journalNumber">The journal.</param>
    public void MarkPosted(string journalNumber)
    {
        Status = PendingCreditNoteStatus.Posted;
        JournalNumber = journalNumber;
    }

    /// <summary>Marks it rejected.</summary>
    public void Reject() => Status = PendingCreditNoteStatus.Rejected;
}
