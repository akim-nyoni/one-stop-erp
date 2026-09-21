using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>The state of a pending tender (brief §RCP-7).</summary>
public enum PendingTenderStatus
{
    /// <summary>Claimed but not yet confirmed on the bank statement.</summary>
    Pending = 1,

    /// <summary>Confirmed — cleared from the uncleared-effects control to the bank.</summary>
    Confirmed = 2,
}

/// <summary>
/// A pending/unconfirmed tender (brief §RCP-7, <c>rcp.pending_tender</c>): a bank transfer or cheque
/// claimed at the counter that sits in the uncleared-effects control account until the bank statement
/// confirms it, then clears to the bank. Reported (aged) by RC33 until confirmed.
/// </summary>
public sealed class PendingTender : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PendingTender()
    {
        ReceiptNumber = string.Empty;
        PendingAccountCode = string.Empty;
    }

    /// <summary>Creates a pending tender.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="receiptId">The receipt.</param>
    /// <param name="receiptNumber">The receipt number.</param>
    /// <param name="tenderType">The tender type.</param>
    /// <param name="reference">The tender reference.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The receipt date.</param>
    /// <param name="pendingAccountCode">The uncleared-effects control account holding it.</param>
    public PendingTender(long companyId, long receiptId, string receiptNumber, TenderType tenderType, string? reference, decimal amount, DateOnly date, string pendingAccountCode)
    {
        CompanyId = companyId;
        ReceiptId = receiptId;
        ReceiptNumber = receiptNumber;
        TenderType = tenderType;
        Reference = reference;
        Amount = amount;
        Date = date;
        PendingAccountCode = pendingAccountCode;
        Status = PendingTenderStatus.Pending;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The receipt.</summary>
    public long ReceiptId { get; private set; }

    /// <summary>The receipt number.</summary>
    public string ReceiptNumber { get; private set; }

    /// <summary>The tender type.</summary>
    public TenderType TenderType { get; private set; }

    /// <summary>The tender reference.</summary>
    public string? Reference { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The receipt date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The uncleared-effects control account holding it.</summary>
    public string PendingAccountCode { get; private set; }

    /// <summary>The status.</summary>
    public PendingTenderStatus Status { get; private set; }

    /// <summary>The date the tender was confirmed on the statement, if any.</summary>
    public DateOnly? ConfirmedOn { get; private set; }

    /// <summary>Marks the tender confirmed on the bank statement.</summary>
    /// <param name="confirmedOn">The confirmation date.</param>
    public void Confirm(DateOnly confirmedOn)
    {
        Status = PendingTenderStatus.Confirmed;
        ConfirmedOn = confirmedOn;
    }
}
