using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>Where a credit-balance refund request stands.</summary>
public enum ReceiptRefundRequestStatus
{
    /// <summary>Asked for; nothing has been paid.</summary>
    Pending = 0,

    /// <summary>Approved by someone other than who asked, and paid.</summary>
    Approved = 1,

    /// <summary>Turned down, with the reason.</summary>
    Rejected = 2,
}

/// <summary>
/// A request to refund a payer's credit balance (<c>rcp.receipt_refund_request</c>, brief §RCP-9): asked for by one user and paid - from a
/// bank account in the credit's own currency - only when another user holding the receipts approval right approves it. One currency per
/// request: credits in other currencies are refunded by their own requests.
/// </summary>
public sealed class ReceiptRefundRequest : AuditableEntity, ICompanyScoped
{
    private ReceiptRefundRequest()
    {
        BankAccountCode = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The payer whose credit is refunded.</param>
    /// <param name="currency">The currency of the credit refunded, and of the payment.</param>
    /// <param name="amount">The amount, in that currency.</param>
    /// <param name="date">The date the refund is to be paid on.</param>
    /// <param name="bankAccountCode">The bank account it is paid from.</param>
    /// <param name="reason">Why.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    public ReceiptRefundRequest(long companyId, long customerId, CurrencyCode currency, decimal amount, DateOnly date, string bankAccountCode, string reason, long requestedByUserId)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        Currency = currency;
        Amount = amount;
        Date = date;
        BankAccountCode = bankAccountCode;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the payer whose credit is refunded.</summary>
    public long CustomerId { get; private set; }

    /// <summary>Gets the currency of the credit refunded, and of the payment.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the amount, in that currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets the date the refund is to be paid on.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the bank account it is paid from.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>Gets why.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public ReceiptRefundRequestStatus Status { get; private set; }

    /// <summary>Gets who decided it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets why it was turned down.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Gets the refund's journal, once paid.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>Records the approval (the caller has paid the refund).</summary>
    /// <param name="userId">Who approved it.</param>
    /// <param name="journalNumber">The refund's journal.</param>
    public void Approve(long userId, string journalNumber)
    {
        Status = ReceiptRefundRequestStatus.Approved;
        DecidedByUserId = userId;
        JournalNumber = journalNumber;
    }

    /// <summary>Turns it down.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    public void Reject(long userId, string reason)
    {
        Status = ReceiptRefundRequestStatus.Rejected;
        DecidedByUserId = userId;
        RejectionReason = reason;
    }
}
