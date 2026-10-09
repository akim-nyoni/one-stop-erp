using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>Where a virement stands.</summary>
public enum VirementStatus
{
    /// <summary>Requested and waiting for approval.</summary>
    Pending = 0,

    /// <summary>Approved and applied to the budget.</summary>
    Approved = 1,

    /// <summary>Rejected; the budget is unchanged.</summary>
    Rejected = 2,
}

/// <summary>
/// A virement (<c>bud.budget_virement</c>, brief §4.18.4): a transfer of budget from one line to another within an active
/// budget, requested with a justification and approved by a different user. An approved virement adds a pair of budget
/// lines that carry its id, so the approved budget is never edited and the virement register can always reconcile
/// original ± virements = current.
/// </summary>
public sealed class BudgetVirement : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BudgetVirement()
    {
        FromAccountCode = string.Empty;
        ToAccountCode = string.Empty;
        Justification = string.Empty;
    }

    /// <summary>Requests a virement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The active budget it moves money within.</param>
    /// <param name="fromAccountCode">The line giving up budget.</param>
    /// <param name="toAccountCode">The line receiving it.</param>
    /// <param name="period">The period the budget moves in.</param>
    /// <param name="amount">The amount; positive.</param>
    /// <param name="justification">Why.</param>
    /// <param name="requestedByUserId">Who asked.</param>
    public BudgetVirement(long companyId, long budgetId, string fromAccountCode, string toAccountCode, int period, decimal amount, string justification, long requestedByUserId)
    {
        CompanyId = companyId;
        BudgetId = budgetId;
        FromAccountCode = fromAccountCode;
        ToAccountCode = toAccountCode;
        Period = period;
        Amount = amount;
        Justification = justification;
        RequestedByUserId = requestedByUserId;
        Status = VirementStatus.Pending;
    }

    /// <summary>Gets the cost centre the budget leaves (null: the line without one).</summary>
    public string? FromCostCentre { get; init; }

    /// <summary>Gets the cost centre the budget goes to.</summary>
    public string? ToCostCentre { get; init; }

    /// <summary>Gets the budget receiving it when it moves to another department's budget (null: the same budget).</summary>
    public long? ToBudgetId { get; init; }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The budget.</summary>
    public long BudgetId { get; private set; }

    /// <summary>The line giving up budget.</summary>
    public string FromAccountCode { get; private set; }

    /// <summary>The line receiving it.</summary>
    public string ToAccountCode { get; private set; }

    /// <summary>The period the budget moves in.</summary>
    public int Period { get; private set; }

    /// <summary>The amount moved.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Why it was requested.</summary>
    public string Justification { get; private set; }

    /// <summary>Who requested it.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public VirementStatus Status { get; private set; }

    /// <summary>Who approved or rejected it.</summary>
    public long? ReviewedByUserId { get; private set; }

    /// <summary>Why it was rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Approves the virement; never by the requester.</summary>
    /// <param name="reviewerUserId">The approver.</param>
    /// <returns>Whether it was approved.</returns>
    public bool Approve(long reviewerUserId)
    {
        if (Status != VirementStatus.Pending || reviewerUserId == RequestedByUserId)
        {
            return false;
        }

        Status = VirementStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        return true;
    }

    /// <summary>Rejects the virement.</summary>
    /// <param name="reviewerUserId">Who rejected it.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Whether it was rejected.</returns>
    public bool Reject(long reviewerUserId, string reason)
    {
        if (Status != VirementStatus.Pending)
        {
            return false;
        }

        Status = VirementStatus.Rejected;
        ReviewedByUserId = reviewerUserId;
        RejectionReason = reason;
        return true;
    }
}

/// <summary>A virement approval band (<c>bud.virement_band</c>): virements up to the amount need an approver with the authorisation level.</summary>
public sealed class VirementBand : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private VirementBand()
    {
    }

    /// <summary>Creates a band.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="upToAmount">The largest amount in the band (null: no limit).</param>
    /// <param name="level">The authorisation level (1–5).</param>
    public VirementBand(long companyId, decimal? upToAmount, int level)
    {
        CompanyId = companyId;
        UpToAmount = upToAmount;
        Level = level;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the largest amount in the band (null: no limit).</summary>
    public decimal? UpToAmount { get; private set; }

    /// <summary>Gets the authorisation level the approver needs.</summary>
    public int Level { get; private set; }
}
