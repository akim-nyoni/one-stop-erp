using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a request to raise a credit limit stands.</summary>
public enum CreditLimitRequestStatus
{
    /// <summary>Waiting for a second user.</summary>
    Pending = 0,

    /// <summary>Approved and applied.</summary>
    Approved = 1,

    /// <summary>Rejected; the limit is unchanged.</summary>
    Rejected = 2,
}

/// <summary>
/// A request to raise a customer's credit limit (<c>ar.credit_limit_request</c>, brief §4.5 credit limit approval
/// workflow): raising a limit extends the company's risk, so one user asks and a different user approves; lowering a
/// limit only reduces risk and needs no approval.
/// </summary>
public sealed class CreditLimitRequest : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CreditLimitRequest()
    {
        Reason = string.Empty;
    }

    /// <summary>Requests a higher limit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="currentLimit">The limit now (0 = unlimited).</param>
    /// <param name="requestedLimit">The limit asked for (0 = unlimited).</param>
    /// <param name="reason">Why.</param>
    /// <param name="requestedByUserId">Who asked.</param>
    public CreditLimitRequest(long companyId, long customerId, decimal currentLimit, decimal requestedLimit, string reason, long requestedByUserId)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        CurrentLimit = currentLimit;
        RequestedLimit = requestedLimit;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
        Status = CreditLimitRequestStatus.Pending;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The limit when asked (0 = unlimited).</summary>
    public decimal CurrentLimit { get; private set; }

    /// <summary>The limit asked for (0 = unlimited).</summary>
    public decimal RequestedLimit { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>Who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public CreditLimitRequestStatus Status { get; private set; }

    /// <summary>Who approved or rejected it.</summary>
    public long? ReviewedByUserId { get; private set; }

    /// <summary>Why it was rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Approves the request; never by the requester.</summary>
    /// <param name="reviewerUserId">The approver.</param>
    /// <returns>Whether it was approved.</returns>
    public bool Approve(long reviewerUserId)
    {
        if (Status != CreditLimitRequestStatus.Pending || reviewerUserId == RequestedByUserId)
        {
            return false;
        }

        Status = CreditLimitRequestStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        return true;
    }

    /// <summary>Rejects the request.</summary>
    /// <param name="reviewerUserId">Who rejected it.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Whether it was pending.</returns>
    public bool Reject(long reviewerUserId, string reason)
    {
        if (Status != CreditLimitRequestStatus.Pending)
        {
            return false;
        }

        Status = CreditLimitRequestStatus.Rejected;
        ReviewedByUserId = reviewerUserId;
        RejectionReason = reason;
        return true;
    }

    /// <summary>Whether a new limit is higher than the current one, where 0 means unlimited.</summary>
    /// <param name="current">The current limit.</param>
    /// <param name="proposed">The proposed limit.</param>
    /// <returns>True when the proposed limit allows more credit.</returns>
    public static bool IsIncrease(decimal current, decimal proposed) => current != 0m && (proposed == 0m || proposed > current);
}
