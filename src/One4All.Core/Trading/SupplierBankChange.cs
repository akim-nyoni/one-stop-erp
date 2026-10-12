using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a requested change of supplier bank details stands.</summary>
public enum BankChangeStatus
{
    /// <summary>Requested and waiting for a second user.</summary>
    Pending = 0,

    /// <summary>Approved by a second user and applied to the supplier.</summary>
    Approved = 1,

    /// <summary>Rejected; the supplier's details are unchanged.</summary>
    Rejected = 2,
}

/// <summary>
/// A requested change of a supplier's bank details (<c>ap.supplier_bank_change</c>, brief §4.6). Changing where a supplier
/// is paid is a classic fraud vector, so it is a dual-authorisation event: one user requests, a different user approves,
/// and only then do the supplier's details change. While a change is pending the supplier is left out of payment runs.
/// </summary>
public sealed class SupplierBankChange : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SupplierBankChange()
    {
    }

    /// <summary>Requests a change.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="accountName">The new account name.</param>
    /// <param name="accountNumber">The new account number.</param>
    /// <param name="bankName">The new bank and branch.</param>
    /// <param name="requestedByUserId">Who asked for it.</param>
    public SupplierBankChange(long companyId, long supplierId, string? accountName, string? accountNumber, string? bankName, long requestedByUserId)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        AccountName = accountName;
        AccountNumber = accountNumber;
        BankName = bankName;
        RequestedByUserId = requestedByUserId;
        Status = BankChangeStatus.Pending;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The new account name.</summary>
    public string? AccountName { get; private set; }

    /// <summary>The new account number.</summary>
    public string? AccountNumber { get; private set; }

    /// <summary>The new bank and branch.</summary>
    public string? BankName { get; private set; }

    /// <summary>Gets the new mobile-money wallet (null: unchanged; empty: removed).</summary>
    public string? MobileMoneyNumber { get; init; }

    /// <summary>Who asked for the change.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Where the change stands.</summary>
    public BankChangeStatus Status { get; private set; }

    /// <summary>Who approved or rejected it; never the requester.</summary>
    public long? ReviewedByUserId { get; private set; }

    /// <summary>Why it was rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Approves the change. Refuses a change that is not pending or a reviewer who requested it.</summary>
    /// <param name="reviewerUserId">The approver.</param>
    /// <returns>Whether it was approved.</returns>
    public bool Approve(long reviewerUserId)
    {
        if (Status != BankChangeStatus.Pending || reviewerUserId == RequestedByUserId)
        {
            return false;
        }

        Status = BankChangeStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        return true;
    }

    /// <summary>Rejects the change. The requester may withdraw their own request.</summary>
    /// <param name="reviewerUserId">Who rejected it.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Whether it was rejected.</returns>
    public bool Reject(long reviewerUserId, string reason)
    {
        if (Status != BankChangeStatus.Pending)
        {
            return false;
        }

        Status = BankChangeStatus.Rejected;
        ReviewedByUserId = reviewerUserId;
        RejectionReason = reason;
        return true;
    }
}
