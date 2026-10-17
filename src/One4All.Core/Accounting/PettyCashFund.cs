using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A petty cash fund on the imprest system (brief §4.7, <c>cb.petty_cash_fund</c>): a fixed float kept in a
/// ledger account, reimbursed from a bank account back to the float amount, counted and reconciled by its
/// custodian.
/// </summary>
public sealed class PettyCashFund : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PettyCashFund()
    {
        Code = string.Empty;
        Name = string.Empty;
        AccountCode = string.Empty;
        BankAccountCode = string.Empty;
    }

    /// <summary>Creates a fund.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The fund code (unique per company).</param>
    /// <param name="name">The fund name.</param>
    /// <param name="accountCode">The petty cash ledger account.</param>
    /// <param name="bankAccountCode">The bank ledger account reimbursements are drawn from.</param>
    /// <param name="floatAmount">The fixed float (imprest) amount.</param>
    public PettyCashFund(long companyId, string code, string name, string accountCode, string bankAccountCode, decimal floatAmount)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        AccountCode = accountCode;
        BankAccountCode = bankAccountCode;
        FloatAmount = floatAmount;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The fund code.</summary>
    public string Code { get; private set; }

    /// <summary>The fund name.</summary>
    public string Name { get; set; }

    /// <summary>The petty cash ledger account.</summary>
    public string AccountCode { get; set; }

    /// <summary>The bank ledger account reimbursements are drawn from.</summary>
    public string BankAccountCode { get; set; }

    /// <summary>The ledger account that takes count shortages and overages; null falls back to the company default.</summary>
    public string? OverShortAccountCode { get; set; }

    /// <summary>The fixed float (imprest) amount.</summary>
    public decimal FloatAmount { get; set; }

    /// <summary>The custodian (user) responsible for the fund, when named.</summary>
    public long? CustodianUserId { get; set; }

    /// <summary>The branch the fund belongs to, when any.</summary>
    public long? BranchId { get; set; }

    /// <summary>Whether the fund is in use.</summary>
    public bool IsActive { get; set; }
}

/// <summary>The state of a petty cash count.</summary>
public enum PettyCashCountStatus
{
    /// <summary>Counted; the variance is known but nothing has been posted.</summary>
    Counted = 1,

    /// <summary>Reconciled: the variance is posted and the float reimbursed.</summary>
    Reconciled = 2,
}

/// <summary>
/// A physical count of a petty cash fund (<c>cb.petty_cash_count</c>): the cash counted against the book
/// balance at the date, the variance, and — once reconciled — the variance and reimbursement journals.
/// </summary>
public sealed class PettyCashCount : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PettyCashCount()
    {
    }

    /// <summary>Records a count.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="fundId">The fund counted.</param>
    /// <param name="countDate">The date of the count.</param>
    /// <param name="countedAmount">The cash counted.</param>
    /// <param name="bookBalance">The ledger balance of the fund at the date.</param>
    /// <param name="countedByUserId">Who counted.</param>
    /// <param name="denominations">The denomination breakdown, as entered.</param>
    /// <param name="note">A note (explanations of the variance).</param>
    public PettyCashCount(long companyId, long fundId, DateOnly countDate, decimal countedAmount, decimal bookBalance, long countedByUserId, string? denominations, string? note)
    {
        CompanyId = companyId;
        FundId = fundId;
        CountDate = countDate;
        CountedAmount = countedAmount;
        BookBalance = bookBalance;
        CountedByUserId = countedByUserId;
        Denominations = denominations;
        Note = note;
        Status = PettyCashCountStatus.Counted;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The fund counted.</summary>
    public long FundId { get; private set; }

    /// <summary>The date of the count.</summary>
    public DateOnly CountDate { get; private set; }

    /// <summary>The cash counted.</summary>
    public decimal CountedAmount { get; private set; }

    /// <summary>The ledger balance of the fund at the date.</summary>
    public decimal BookBalance { get; private set; }

    /// <summary>Counted minus book: positive is an overage, negative a shortage.</summary>
    public decimal Variance => CountedAmount - BookBalance;

    /// <summary>Who counted.</summary>
    public long CountedByUserId { get; private set; }

    /// <summary>The denomination breakdown, as entered.</summary>
    public string? Denominations { get; private set; }

    /// <summary>A note.</summary>
    public string? Note { get; private set; }

    /// <summary>The state of the count.</summary>
    public PettyCashCountStatus Status { get; private set; }

    /// <summary>The journal that posted the variance, when there was one.</summary>
    public string? VarianceJournalNumber { get; private set; }

    /// <summary>The journal that reimbursed the float, when the float was short.</summary>
    public string? ReimbursementJournalNumber { get; private set; }

    /// <summary>Who reconciled.</summary>
    public long? ReconciledByUserId { get; private set; }

    /// <summary>When the count was reconciled.</summary>
    public DateTimeOffset? ReconciledOnUtc { get; private set; }

    /// <summary>Marks the count reconciled with the journals it produced.</summary>
    /// <param name="userId">Who reconciled.</param>
    /// <param name="varianceJournal">The variance journal, if any.</param>
    /// <param name="reimbursementJournal">The reimbursement journal, if any.</param>
    public void Reconcile(long userId, string? varianceJournal, string? reimbursementJournal)
    {
        Status = PettyCashCountStatus.Reconciled;
        ReconciledByUserId = userId;
        ReconciledOnUtc = DateTimeOffset.UtcNow;
        VarianceJournalNumber = varianceJournal;
        ReimbursementJournalNumber = reimbursementJournal;
    }
}
