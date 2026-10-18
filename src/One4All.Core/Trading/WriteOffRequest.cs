using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a write-off request stands.</summary>
public enum WriteOffRequestStatus
{
    /// <summary>Asked for; the debt is still owed.</summary>
    Pending = 0,

    /// <summary>Approved by someone other than who asked; the debt is written off.</summary>
    Approved = 1,

    /// <summary>Turned down, with the reason.</summary>
    Rejected = 2,
}

/// <summary>
/// A request to write off a customer's bad debt (<c>ar.write_off_request</c>, brief §4.5 and §7 "write-offs"): asked for by one user and written off - with the VAT
/// relief when it is overdue long enough - only when another approves it, in the inbox when the company has a write-off workflow.
/// </summary>
public sealed class WriteOffRequest : AuditableEntity, ICompanyScoped
{
    private WriteOffRequest()
    {
        InvoiceNumber = string.Empty;
        BadDebtAccountCode = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The invoice.</param>
    /// <param name="invoiceNumber">Its number.</param>
    /// <param name="amount">What is open on it when asked.</param>
    /// <param name="badDebtAccountCode">The bad debt expense account.</param>
    /// <param name="reason">Why.</param>
    /// <param name="minimumDaysOverdue">How overdue it must be for the VAT relief.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    public WriteOffRequest(long companyId, long openItemId, string invoiceNumber, decimal amount, string badDebtAccountCode, string reason, int minimumDaysOverdue, long requestedByUserId)
    {
        CompanyId = companyId;
        OpenItemId = openItemId;
        InvoiceNumber = invoiceNumber;
        Amount = amount;
        BadDebtAccountCode = badDebtAccountCode;
        Reason = reason;
        MinimumDaysOverdue = minimumDaysOverdue;
        RequestedByUserId = requestedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the invoice.</summary>
    public long OpenItemId { get; private set; }

    /// <summary>Gets its number.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>Gets what was open on it when asked.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets the bad debt expense account.</summary>
    public string BadDebtAccountCode { get; private set; }

    /// <summary>Gets why.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets how overdue it must be for the VAT relief.</summary>
    public int MinimumDaysOverdue { get; private set; }

    /// <summary>Gets who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public WriteOffRequestStatus Status { get; private set; }

    /// <summary>Gets who decided it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets why it was turned down.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Records the approval (the caller has written the debt off).</summary>
    /// <param name="userId">Who approved it.</param>
    public void Approve(long userId)
    {
        Status = WriteOffRequestStatus.Approved;
        DecidedByUserId = userId;
    }

    /// <summary>Turns it down.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    public void Reject(long userId, string reason)
    {
        Status = WriteOffRequestStatus.Rejected;
        DecidedByUserId = userId;
        RejectionReason = reason;
    }
}
