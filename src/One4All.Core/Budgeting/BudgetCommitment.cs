using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>The status of a budget commitment (encumbrance).</summary>
public enum CommitmentStatus
{
    /// <summary>Open - the amount is reserved against the budget and reduces the available balance.</summary>
    Open = 0,

    /// <summary>Released - the reservation has been freed (e.g. the order was invoiced or cancelled).</summary>
    Released = 1,

    /// <summary>Lapsed at year end: the reservation ends with the year that made it.</summary>
    Lapsed = 2,

    /// <summary>Carried forward at year end into a commitment against the next year's budget.</summary>
    CarriedForward = 3,
}

/// <summary>
/// A budget commitment / encumbrance (brief §4.18 Commitment Control): an amount reserved against a budget
/// line when spend is committed (typically a purchase order) but not yet actual. While open it reduces the
/// available balance - available = budget − actual − open commitments - so a later order cannot over-commit
/// the same funds. When the commitment turns into actual (an invoice posts) or is cancelled, it is released.
/// Commitments never post to the GL; like budgets they are a control-side record joined by dimensions.
/// </summary>
public sealed class BudgetCommitment : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BudgetCommitment()
    {
        AccountCode = string.Empty;
        Reference = string.Empty;
    }

    /// <summary>Raises a commitment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="type">The budget type/scenario the commitment is checked against.</param>
    /// <param name="department">The department (null for company-wide).</param>
    /// <param name="accountCode">The account committed against.</param>
    /// <param name="period">The period (month 1–12) the commitment falls in.</param>
    /// <param name="amount">The committed amount (positive).</param>
    /// <param name="reference">The source reference (e.g. the purchase-order number).</param>
    public BudgetCommitment(long companyId, long fiscalYearId, BudgetType type, string? department, string accountCode, int period, decimal amount, string reference)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        Type = type;
        Department = department;
        AccountCode = accountCode;
        Period = period;
        Amount = amount;
        Reference = reference;
        Status = CommitmentStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The fiscal year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>The budget type/scenario.</summary>
    public BudgetType Type { get; private set; }

    /// <summary>The department (null for company-wide).</summary>
    public string? Department { get; private set; }

    /// <summary>The account committed against.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The period (month 1–12).</summary>
    public int Period { get; private set; }

    /// <summary>The committed amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The source reference (e.g. the purchase-order number).</summary>
    public string Reference { get; private set; }

    /// <summary>The status.</summary>
    public CommitmentStatus Status { get; private set; }

    /// <summary>The prior year's commitment this one was carried forward from, if any.</summary>
    public long? CarriedFromId { get; private set; }

    /// <summary>Who approved the year-end decision (lapse or carry forward), once made.</summary>
    public long? YearEndApprovedByUserId { get; private set; }

    /// <summary>Uses up part of an open commitment as it is spent, releasing it when nothing is left.</summary>
    /// <param name="amount">The amount spent.</param>
    /// <returns>How much of the commitment was used up.</returns>
    public decimal Reduce(decimal amount)
    {
        if (Status != CommitmentStatus.Open || amount <= 0m)
        {
            return 0m;
        }

        decimal taken = Math.Min(amount, Amount);
        Amount -= taken;
        if (Amount == 0m)
        {
            Status = CommitmentStatus.Released;
        }

        return taken;
    }

    /// <summary>Releases the commitment, freeing the reserved budget.</summary>
    /// <returns>True when it was open and is now released; false when it was not open.</returns>
    public bool Release()
    {
        if (Status != CommitmentStatus.Open)
        {
            return false;
        }

        Status = CommitmentStatus.Released;
        return true;
    }

    /// <summary>Lets an open commitment lapse at year end.</summary>
    /// <param name="approvedByUserId">Who approved the year-end decision.</param>
    /// <returns>Whether it was open.</returns>
    public bool Lapse(long approvedByUserId) => CloseForYearEnd(CommitmentStatus.Lapsed, approvedByUserId);

    /// <summary>
    /// Carries an open commitment forward: it closes here and reopens against the next year's budget, in its first period.
    /// </summary>
    /// <param name="nextFiscalYearId">The next fiscal year.</param>
    /// <param name="approvedByUserId">Who approved the carry-forward.</param>
    /// <returns>The new commitment, or null when this one was not open.</returns>
    public BudgetCommitment? CarryForward(long nextFiscalYearId, long approvedByUserId)
    {
        if (!CloseForYearEnd(CommitmentStatus.CarriedForward, approvedByUserId))
        {
            return null;
        }

        return new BudgetCommitment(CompanyId, nextFiscalYearId, Type, Department, AccountCode, 1, Amount, Reference) { CarriedFromId = Id };
    }

    private bool CloseForYearEnd(CommitmentStatus status, long approvedByUserId)
    {
        if (Status != CommitmentStatus.Open)
        {
            return false;
        }

        Status = status;
        YearEndApprovedByUserId = approvedByUserId;
        return true;
    }
}
