namespace One4All.Core.Accounting;

/// <summary>The five fundamental account types (brief §3.1). Determines the normal balance side and
/// whether the account belongs to the balance sheet or the income statement.</summary>
public enum AccountType
{
    /// <summary>Asset — normal debit balance, balance sheet.</summary>
    Asset = 1,

    /// <summary>Liability — normal credit balance, balance sheet.</summary>
    Liability = 2,

    /// <summary>Equity — normal credit balance, balance sheet.</summary>
    Equity = 3,

    /// <summary>Income — normal credit balance, income statement.</summary>
    Income = 4,

    /// <summary>Expense — normal debit balance, income statement.</summary>
    Expense = 5,
}

/// <summary>The side of the ledger on which an account normally carries its balance.</summary>
public enum NormalBalance
{
    /// <summary>Debit balance (assets, expenses).</summary>
    Debit = 1,

    /// <summary>Credit balance (liabilities, equity, income).</summary>
    Credit = 2,
}

/// <summary>
/// The kind of control account (brief §3.1). Control accounts are the GL's link to sub-ledgers and are
/// locked from manual journals. <see cref="None"/> marks an ordinary postable account.
/// </summary>
public enum ControlAccountType
{
    /// <summary>Not a control account.</summary>
    None = 0,

    /// <summary>Accounts Receivable control.</summary>
    AccountsReceivable = 1,

    /// <summary>Accounts Payable control.</summary>
    AccountsPayable = 2,

    /// <summary>Stock/inventory control.</summary>
    Stock = 3,

    /// <summary>VAT/Sales-tax input (recoverable).</summary>
    VatInput = 4,

    /// <summary>VAT/Sales-tax output (payable).</summary>
    VatOutput = 5,

    /// <summary>Bank/cash control.</summary>
    Bank = 6,

    /// <summary>Retained earnings.</summary>
    RetainedEarnings = 7,

    /// <summary>Realised/unrealised FX gain.</summary>
    FxGain = 8,

    /// <summary>Realised/unrealised FX loss.</summary>
    FxLoss = 9,

    /// <summary>Suspense.</summary>
    Suspense = 10,

    /// <summary>Work in progress.</summary>
    WorkInProgress = 11,

    /// <summary>Retention.</summary>
    Retention = 12,

    /// <summary>Goods received not invoiced.</summary>
    GoodsReceivedNotInvoiced = 13,

    /// <summary>Rounding differences.</summary>
    Rounding = 14,

    /// <summary>Payroll net-pay control.</summary>
    PayrollControl = 15,
}

/// <summary>Helpers for account-type semantics.</summary>
public static class AccountTypeExtensions
{
    /// <summary>Returns the normal balance side for an account type.</summary>
    /// <param name="type">The account type.</param>
    /// <returns>The normal balance side.</returns>
    public static NormalBalance NormalBalance(this AccountType type) => type switch
    {
        AccountType.Asset or AccountType.Expense => Accounting.NormalBalance.Debit,
        _ => Accounting.NormalBalance.Credit,
    };

    /// <summary>True when the account type appears on the balance sheet.</summary>
    /// <param name="type">The account type.</param>
    /// <returns>True for asset/liability/equity.</returns>
    public static bool IsBalanceSheet(this AccountType type) =>
        type is AccountType.Asset or AccountType.Liability or AccountType.Equity;

    /// <summary>True when the account type appears on the income statement.</summary>
    /// <param name="type">The account type.</param>
    /// <returns>True for income/expense.</returns>
    public static bool IsIncomeStatement(this AccountType type) =>
        type is AccountType.Income or AccountType.Expense;
}
