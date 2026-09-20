using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>What kind of refund was paid (brief §RCP-9).</summary>
public enum RefundType
{
    /// <summary>A payer's credit balance / overpayment refunded.</summary>
    CreditBalance = 1,

    /// <summary>A deposit released (less any forfeit).</summary>
    Deposit = 2,
}

/// <summary>
/// A refund paid to a payer (brief §RCP-9, <c>rcp.refund</c>). Persisted when a credit balance or deposit is
/// refunded, so the refunds register (RC41) and the audit trail have a record — paid through the bank/safe,
/// never the till.
/// </summary>
public sealed class RefundRecord : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RefundRecord()
    {
        PayerName = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates a refund record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="type">The refund type.</param>
    /// <param name="payerName">The payer (or a description for a deposit refund).</param>
    /// <param name="amount">The net amount refunded.</param>
    /// <param name="date">The refund date.</param>
    /// <param name="reason">The reason.</param>
    /// <param name="approverUserId">The approving user.</param>
    /// <param name="journalNumber">The GL journal number.</param>
    public RefundRecord(long companyId, RefundType type, string payerName, decimal amount, DateOnly date, string reason, long approverUserId, string journalNumber)
    {
        CompanyId = companyId;
        Type = type;
        PayerName = payerName;
        Amount = amount;
        Date = date;
        Reason = reason;
        ApproverUserId = approverUserId;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The refund type.</summary>
    public RefundType Type { get; private set; }

    /// <summary>The payer (or a description for a deposit refund).</summary>
    public string PayerName { get; private set; }

    /// <summary>The net amount refunded.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The refund date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The reason.</summary>
    public string Reason { get; private set; }

    /// <summary>The approving user.</summary>
    public long ApproverUserId { get; private set; }

    /// <summary>The GL journal number.</summary>
    public string JournalNumber { get; private set; } = string.Empty;
}
