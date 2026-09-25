using One4All.Core.Common;

namespace One4All.Core.Expenses;

/// <summary>The lifecycle of an expense claim.</summary>
public enum ExpenseClaimStatus
{
    /// <summary>Being prepared.</summary>
    Draft = 1,

    /// <summary>Approved for payment.</summary>
    Approved = 2,

    /// <summary>Reimbursed to the claimant.</summary>
    Reimbursed = 3,

    /// <summary>Rejected.</summary>
    Rejected = 4,
}

/// <summary>
/// An employee expense claim (<c>exp.claim</c>): a set of expense lines submitted for reimbursement. Approval
/// gates payment; reimbursement posts the expenses and pays the claimant through the central posting engine.
/// </summary>
public sealed class ExpenseClaim : AuditableEntity, ICompanyScoped
{
    private readonly List<ExpenseClaimLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ExpenseClaim() => Reference = string.Empty;

    /// <summary>Creates a draft claim.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The claimant employee.</param>
    /// <param name="reference">The claim reference.</param>
    /// <param name="claimDate">The claim date.</param>
    public ExpenseClaim(long companyId, long employeeId, string reference, DateOnly claimDate)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Reference = reference;
        ClaimDate = claimDate;
        Status = ExpenseClaimStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The claimant employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The claim reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The claim date.</summary>
    public DateOnly ClaimDate { get; private set; }

    /// <summary>The status.</summary>
    public ExpenseClaimStatus Status { get; private set; }

    /// <summary>The approver, if approved.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>The reimbursement GL journal number, if reimbursed.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>The claim lines.</summary>
    public IReadOnlyList<ExpenseClaimLine> Lines => _lines.AsReadOnly();

    /// <summary>The claim total.</summary>
    public decimal Total => _lines.Sum(l => l.Amount);

    /// <summary>Adds an expense line.</summary>
    /// <param name="description">The line description.</param>
    /// <param name="expenseAccountCode">The expense account debited on reimbursement.</param>
    /// <param name="amount">The amount (positive).</param>
    public void AddLine(string description, string expenseAccountCode, decimal amount) => _lines.Add(new ExpenseClaimLine(description, expenseAccountCode, amount));

    /// <summary>Approves the claim.</summary>
    /// <param name="approvedByUserId">The approver.</param>
    public void Approve(long approvedByUserId)
    {
        Status = ExpenseClaimStatus.Approved;
        ApprovedByUserId = approvedByUserId;
    }

    /// <summary>Rejects the claim.</summary>
    public void Reject() => Status = ExpenseClaimStatus.Rejected;

    /// <summary>Marks the claim reimbursed, recording the payment journal.</summary>
    /// <param name="journalNumber">The reimbursement journal number.</param>
    public void MarkReimbursed(string journalNumber)
    {
        Status = ExpenseClaimStatus.Reimbursed;
        JournalNumber = journalNumber;
    }
}

/// <summary>One line of an expense claim (<c>exp.claim_line</c>).</summary>
public sealed class ExpenseClaimLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ExpenseClaimLine()
    {
        Description = string.Empty;
        ExpenseAccountCode = string.Empty;
    }

    /// <summary>Creates a claim line.</summary>
    /// <param name="description">The line description.</param>
    /// <param name="expenseAccountCode">The expense account.</param>
    /// <param name="amount">The amount.</param>
    public ExpenseClaimLine(string description, string expenseAccountCode, decimal amount)
    {
        Description = description;
        ExpenseAccountCode = expenseAccountCode;
        Amount = amount;
    }

    /// <summary>The owning claim id.</summary>
    public long ExpenseClaimId { get; private set; }

    /// <summary>The line description.</summary>
    public string Description { get; private set; }

    /// <summary>The expense account debited on reimbursement.</summary>
    public string ExpenseAccountCode { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }
}
