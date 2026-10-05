using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A nominal (general-ledger) account in the chart of accounts (brief §3.1, <c>gl.account</c>). The base
/// account code is combined with cost-centre and department dimensions at posting time to form the
/// composite <see cref="AccountKey"/>. Control accounts link the GL to sub-ledgers and are locked from
/// manual journals (enforced here and by a database trigger as a second net).
/// </summary>
public sealed class LedgerAccount : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LedgerAccount()
    {
        AccountCode = string.Empty;
        Name = string.Empty;
        ReportCategory = string.Empty;
    }

    /// <summary>Creates a ledger account.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="accountCode">The unique account code within the company (up to 8 chars).</param>
    /// <param name="name">The account name.</param>
    /// <param name="type">The account type.</param>
    /// <param name="reportCategory">The financial-statement mapping category.</param>
    public LedgerAccount(long companyId, string accountCode, string name, AccountType type, string? reportCategory = null)
    {
        CompanyId = companyId;
        AccountCode = accountCode.Trim().ToUpperInvariant();
        Name = name;
        Type = type;
        ReportCategory = reportCategory ?? type.ToString();
        AllowManualJournal = true;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The unique account code within the company.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The account name.</summary>
    public string Name { get; set; }

    /// <summary>The account type (drives normal balance and statement placement).</summary>
    public AccountType Type { get; private set; }

    /// <summary>The report category for financial-statement mapping.</summary>
    public string ReportCategory { get; set; }

    /// <summary>The activity this account's movement represents on the Statement of Cash Flows.</summary>
    public CashFlowCategory CashFlowCategory { get; private set; }

    /// <summary>Whether this is a control account.</summary>
    public bool IsControlAccount => ControlAccountType != ControlAccountType.None;

    /// <summary>The control-account type (<see cref="Accounting.ControlAccountType.None"/> for ordinary accounts).</summary>
    public ControlAccountType ControlAccountType { get; private set; }

    /// <summary>Whether manual journals may be posted to this account (never for control accounts).</summary>
    public bool AllowManualJournal { get; set; }

    /// <summary>Optional currency the account is restricted to (e.g. a foreign bank account).</summary>
    public CurrencyCode? CurrencyRestriction { get; set; }

    /// <summary>Whether the account is active and available for posting.</summary>
    public bool IsActive { get; set; }

    /// <summary>Whether the account participates in budgeting.</summary>
    public bool BudgetEnabled { get; set; }

    /// <summary>How the account is treated when restating for hyperinflation (IAS 29); by account type unless set.</summary>
    public Ias29Classification Ias29 { get; set; }

    /// <summary>The notes-to-the-accounts reference the balance is disclosed under (for example "7" or "7.2"); null when not mapped.</summary>
    public string? NoteReference { get; set; }

    /// <summary>The normal balance side, derived from the account type.</summary>
    public NormalBalance NormalBalance => Type.NormalBalance();

    /// <summary>True when a manual journal may post to this account (active, allowed, and not a control account).</summary>
    public bool IsPostableManually => IsActive && AllowManualJournal && !IsControlAccount;

    /// <summary>Designates this account as a control account of the given type (which also blocks manual journals).</summary>
    /// <param name="controlType">The control-account type.</param>
    public void MakeControlAccount(ControlAccountType controlType)
    {
        ControlAccountType = controlType;
        if (controlType != ControlAccountType.None)
        {
            AllowManualJournal = false;
        }
    }

    /// <summary>Sets the account's cash-flow classification for the Statement of Cash Flows.</summary>
    /// <param name="category">The activity the account's movement represents.</param>
    public void SetCashFlowCategory(CashFlowCategory category) => CashFlowCategory = category;

    /// <summary>Returns true when a posting in <paramref name="currency"/> is permitted on this account.</summary>
    /// <param name="currency">The transaction currency.</param>
    /// <returns>True when allowed by the currency restriction.</returns>
    public bool AllowsCurrency(CurrencyCode currency) =>
        CurrencyRestriction is null || CurrencyRestriction.Value == currency;
}
