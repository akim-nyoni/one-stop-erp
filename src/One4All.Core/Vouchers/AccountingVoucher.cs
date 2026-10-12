using One4All.Core.Common;

namespace One4All.Core.Vouchers;

/// <summary>The kind of accounting voucher.</summary>
public enum AccountingVoucherType
{
    /// <summary>A payment voucher: money out of a bank account to expense or other accounts.</summary>
    Payment = 1,

    /// <summary>A receipt voucher: money into a bank account from income or other accounts.</summary>
    Receipt = 2,

    /// <summary>A journal voucher: a balanced set of debits and credits.</summary>
    Journal = 3,

    /// <summary>A petty cash voucher: a payment out of the petty cash float.</summary>
    PettyCash = 4,
}

/// <summary>Where an accounting voucher stands.</summary>
public enum AccountingVoucherStatus
{
    /// <summary>Prepared and awaiting approval.</summary>
    Draft = 0,

    /// <summary>Approved by a second user and posted.</summary>
    Posted = 1,

    /// <summary>Cancelled before approval.</summary>
    Cancelled = 2,
}

/// <summary>
/// A payment, receipt, journal or petty cash voucher (<c>vou.accounting_voucher</c>, brief §4.8.3): the classic accounting
/// document with its own numbering per type, prepared by one user and approved by another, whose approval posts it, and
/// which prints with signature blocks.
/// </summary>
public sealed class AccountingVoucher : AuditableEntity, ICompanyScoped
{
    private readonly List<AccountingVoucherLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AccountingVoucher()
    {
        Number = string.Empty;
        Party = string.Empty;
    }

    /// <summary>Prepares a voucher.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="type">The kind.</param>
    /// <param name="number">The voucher number.</param>
    /// <param name="date">The voucher date.</param>
    /// <param name="party">Who is paid or pays (or what a journal is about).</param>
    /// <param name="cashAccountCode">The bank or cash account, for payments and receipts.</param>
    /// <param name="narrative">The description.</param>
    /// <param name="preparedByUserId">Who prepared it.</param>
    public AccountingVoucher(long companyId, AccountingVoucherType type, string number, DateOnly date, string party, string? cashAccountCode, string? narrative, long preparedByUserId)
    {
        CompanyId = companyId;
        Type = type;
        Number = number;
        Date = date;
        Party = party;
        CashAccountCode = cashAccountCode;
        Narrative = narrative;
        PreparedByUserId = preparedByUserId;
        Status = AccountingVoucherStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The kind.</summary>
    public AccountingVoucherType Type { get; private set; }

    /// <summary>The voucher number.</summary>
    public string Number { get; private set; }

    /// <summary>The voucher date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Who is paid or pays.</summary>
    public string Party { get; private set; }

    /// <summary>The bank or cash account, for payments and receipts.</summary>
    public string? CashAccountCode { get; private set; }

    /// <summary>The description.</summary>
    public string? Narrative { get; private set; }

    /// <summary>Who prepared it.</summary>
    public long PreparedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public AccountingVoucherStatus Status { get; private set; }

    /// <summary>The journal its approval posted.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>The lines.</summary>
    public IReadOnlyList<AccountingVoucherLine> Lines => _lines.AsReadOnly();

    /// <summary>The voucher amount: total debits.</summary>
    public decimal Total => _lines.Sum(l => l.Debit);

    /// <summary>Adds a line.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="debit">The debit, or zero.</param>
    /// <param name="credit">The credit, or zero.</param>
    /// <param name="narrative">The line description.</param>
    public void AddLine(string accountCode, decimal debit, decimal credit, string? narrative, string? costCentre = null, string? department = null, string? project = null) =>
        _lines.Add(new AccountingVoucherLine(accountCode, debit, credit, narrative) { CostCentre = costCentre, Department = department, Project = project });

    /// <summary>Records the approval and the posting it made.</summary>
    /// <param name="userId">The approver.</param>
    /// <param name="journalNumber">The journal posted.</param>
    public void Approve(long userId, string journalNumber)
    {
        ApprovedByUserId = userId;
        JournalNumber = journalNumber;
        Status = AccountingVoucherStatus.Posted;
    }

    /// <summary>Cancels a draft.</summary>
    public void Cancel() => Status = AccountingVoucherStatus.Cancelled;

    /// <summary>Gets who checked the voucher (the middle signature: prepared, checked, approved), if anyone has.</summary>
    public long? CheckedByUserId { get; private set; }

    /// <summary>Gets when it was checked.</summary>
    public DateTimeOffset? CheckedOnUtc { get; private set; }

    /// <summary>Records the check.</summary>
    /// <param name="userId">The checker.</param>
    /// <param name="at">When.</param>
    public void MarkChecked(long userId, DateTimeOffset at)
    {
        CheckedByUserId = userId;
        CheckedOnUtc = at;
    }
}

/// <summary>A line of an accounting voucher (<c>vou.accounting_voucher_line</c>).</summary>
public sealed class AccountingVoucherLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AccountingVoucherLine() => AccountCode = string.Empty;

    /// <summary>Creates a line.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="debit">The debit, or zero.</param>
    /// <param name="credit">The credit, or zero.</param>
    /// <param name="narrative">The line description.</param>
    public AccountingVoucherLine(string accountCode, decimal debit, decimal credit, string? narrative)
    {
        AccountCode = accountCode;
        Debit = debit;
        Credit = credit;
        Narrative = narrative;
    }

    /// <summary>The voucher.</summary>
    public long AccountingVoucherId { get; private set; }

    /// <summary>The account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The debit.</summary>
    public decimal Debit { get; private set; }

    /// <summary>The credit.</summary>
    public decimal Credit { get; private set; }

    /// <summary>The line description.</summary>
    public string? Narrative { get; private set; }

    /// <summary>The cost centre the line posts to.</summary>
    public string? CostCentre { get; internal init; }

    /// <summary>The department the line posts to.</summary>
    public string? Department { get; internal init; }

    /// <summary>The project the line posts to.</summary>
    public string? Project { get; internal init; }
}
