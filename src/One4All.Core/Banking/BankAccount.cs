using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Banking;

/// <summary>
/// A bank or cash account in the Cash Book (brief §4.7, <c>cb.bank_account</c>). Denominated in one
/// currency and linked to a GL control account; its GL balance is reconciled against imported bank
/// statements.
/// </summary>
public sealed class BankAccount : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BankAccount()
    {
        Code = string.Empty;
        Name = string.Empty;
        GlAccountCode = string.Empty;
    }

    /// <summary>Creates a bank account.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The account code.</param>
    /// <param name="name">The account name.</param>
    /// <param name="currency">The account currency.</param>
    /// <param name="glAccountCode">The linked GL account code.</param>
    public BankAccount(long companyId, string code, string name, CurrencyCode currency, string glAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Currency = currency;
        GlAccountCode = glAccountCode.Trim().ToUpperInvariant();
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The bank account code.</summary>
    public string Code { get; private set; }

    /// <summary>The account name.</summary>
    public string Name { get; set; }

    /// <summary>The account currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The linked GL account code.</summary>
    public string GlAccountCode { get; private set; }

    /// <summary>The bank name.</summary>
    public string? BankName { get; set; }

    /// <summary>The external account number.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a mobile-money wallet rather than a bank account.</summary>
    public bool IsMobileWallet { get; set; }

    /// <summary>Whether the account is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// The date the account is reconciled and signed off to (brief §4.7): nothing can be posted to its ledger account,
    /// imported onto its statement, or unmatched, on or before it.
    /// </summary>
    public DateOnly? ReconciledTo { get; private set; }

    /// <summary>Who signed off the latest reconciliation.</summary>
    public long? ReconciledByUserId { get; private set; }

    /// <summary>The bank statement's closing balance on <see cref="ReconciledTo"/>, as signed off (the next statement opens from it).</summary>
    public decimal? ReconciledStatementBalance { get; private set; }

    /// <summary>Signs off the reconciliation to a date, locking the period up to it.</summary>
    /// <param name="asAt">The date reconciled to.</param>
    /// <param name="userId">Who signed it off.</param>
    /// <param name="statementClosingBalance">The bank statement's closing balance on that date.</param>
    public void SignOff(DateOnly asAt, long userId, decimal statementClosingBalance)
    {
        ReconciledTo = asAt;
        ReconciledByUserId = userId;
        ReconciledStatementBalance = statementClosingBalance;
    }
}

/// <summary>
/// A line imported from a bank statement (<c>cb.bank_statement_line</c>). The signed <see cref="Amount"/> is
/// positive for money in (a deposit, matching a GL debit on the bank account) and negative for money out.
/// Reconciliation matches each line to a GL cash-book posting.
/// </summary>
public sealed class BankStatementLine : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BankStatementLine()
    {
        Description = string.Empty;
    }

    /// <summary>Creates a statement line.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="statementDate">The statement date.</param>
    /// <param name="description">The line description.</param>
    /// <param name="amount">The signed amount (+ in, − out).</param>
    /// <param name="reference">Optional reference.</param>
    public BankStatementLine(long companyId, long bankAccountId, DateOnly statementDate, string description, decimal amount, string? reference = null)
    {
        CompanyId = companyId;
        BankAccountId = bankAccountId;
        StatementDate = statementDate;
        Description = description;
        Amount = amount;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The bank account.</summary>
    public long BankAccountId { get; private set; }

    /// <summary>The statement date.</summary>
    public DateOnly StatementDate { get; private set; }

    /// <summary>The line description.</summary>
    public string Description { get; private set; }

    /// <summary>Optional reference.</summary>
    public string? Reference { get; private set; }

    /// <summary>The signed amount (+ money in, − money out).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Whether the line has been reconciled to a GL posting.</summary>
    public bool IsReconciled { get; private set; }

    /// <summary>The GL journal line this statement line is matched to, if any.</summary>
    public long? MatchedJournalLineId { get; private set; }

    /// <summary>Marks the line reconciled against a GL journal line.</summary>
    /// <param name="journalLineId">The matched GL journal-line id.</param>
    public void MatchTo(long journalLineId)
    {
        IsReconciled = true;
        MatchedJournalLineId = journalLineId;
    }

    /// <summary>Clears the reconciliation.</summary>
    public void Unmatch()
    {
        IsReconciled = false;
        MatchedJournalLineId = null;
    }
}
