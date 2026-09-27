using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// One posting line of a <see cref="JournalEntry"/> (<c>gl.journal_line</c>). Carries the composite
/// account key (account/cost-centre/department), the transaction-currency amount and its exchange rate,
/// the base-currency amount, and an optional second reporting-currency amount (brief §3.2 rule 4).
/// Debit and credit are held as separate non-negative amounts; exactly one side is non-zero.
/// </summary>
public sealed class JournalLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private JournalLine()
    {
        AccountCode = string.Empty;
        CostCentre = string.Empty;
        Department = string.Empty;
    }

    private JournalLine(
        AccountKey key,
        CurrencyCode transactionCurrency,
        decimal exchangeRate,
        decimal transactionDebit,
        decimal transactionCredit,
        decimal baseDebit,
        decimal baseCredit,
        string? narrative)
    {
        AccountCode = key.AccountCode;
        CostCentre = key.CostCentre;
        Department = key.Department;
        TransactionCurrency = transactionCurrency;
        ExchangeRate = exchangeRate;
        TransactionDebit = transactionDebit;
        TransactionCredit = transactionCredit;
        BaseDebit = baseDebit;
        BaseCredit = baseCredit;
        Narrative = narrative;
    }

    /// <summary>The owning journal id.</summary>
    public long JournalEntryId { get; private set; }

    /// <summary>The base nominal account code.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The cost-centre code (empty when unanalysed).</summary>
    public string CostCentre { get; private set; }

    /// <summary>The department code (empty when unanalysed).</summary>
    public string Department { get; private set; }

    /// <summary>The composite posting key.</summary>
    public AccountKey Key => new(AccountCode, CostCentre, Department);

    /// <summary>The transaction currency of this line.</summary>
    public CurrencyCode TransactionCurrency { get; private set; }

    /// <summary>The exchange rate used to convert to base currency.</summary>
    public decimal ExchangeRate { get; private set; }

    /// <summary>Debit amount in transaction currency.</summary>
    public decimal TransactionDebit { get; private set; }

    /// <summary>Credit amount in transaction currency.</summary>
    public decimal TransactionCredit { get; private set; }

    /// <summary>Debit amount in base currency.</summary>
    public decimal BaseDebit { get; private set; }

    /// <summary>Credit amount in base currency.</summary>
    public decimal BaseCredit { get; private set; }

    /// <summary>Optional debit amount in the second reporting currency.</summary>
    public decimal ReportingDebit { get; private set; }

    /// <summary>Optional credit amount in the second reporting currency.</summary>
    public decimal ReportingCredit { get; private set; }

    /// <summary>Optional line narrative.</summary>
    public string? Narrative { get; private set; }

    /// <summary>The tax code this line relates to (set on tax-account lines so VAT can be analysed by rate/code).</summary>
    public string? TaxCode { get; private set; }

    /// <summary>Tags this line with a tax code (for the VAT breakdown by code).</summary>
    /// <param name="taxCode">The tax code, or null.</param>
    public void SetTaxCode(string? taxCode) => TaxCode = taxCode;

    /// <summary>Creates a debit line.</summary>
    /// <param name="key">The composite account key.</param>
    /// <param name="transactionCurrency">The transaction currency.</param>
    /// <param name="transactionAmount">The debit amount in transaction currency.</param>
    /// <param name="exchangeRate">The exchange rate to base.</param>
    /// <param name="baseAmount">The debit amount in base currency.</param>
    /// <param name="narrative">Optional narrative.</param>
    /// <returns>A debit line.</returns>
    public static JournalLine Debit(AccountKey key, CurrencyCode transactionCurrency, decimal transactionAmount, decimal exchangeRate, decimal baseAmount, string? narrative = null) =>
        new(key, transactionCurrency, exchangeRate, transactionAmount, 0m, baseAmount, 0m, narrative);

    /// <summary>Creates a credit line.</summary>
    /// <param name="key">The composite account key.</param>
    /// <param name="transactionCurrency">The transaction currency.</param>
    /// <param name="transactionAmount">The credit amount in transaction currency.</param>
    /// <param name="exchangeRate">The exchange rate to base.</param>
    /// <param name="baseAmount">The credit amount in base currency.</param>
    /// <param name="narrative">Optional narrative.</param>
    /// <returns>A credit line.</returns>
    public static JournalLine Credit(AccountKey key, CurrencyCode transactionCurrency, decimal transactionAmount, decimal exchangeRate, decimal baseAmount, string? narrative = null) =>
        new(key, transactionCurrency, exchangeRate, 0m, transactionAmount, 0m, baseAmount, narrative);

    /// <summary>Sets the second reporting-currency amounts on this line.</summary>
    /// <param name="reportingDebit">Reporting-currency debit.</param>
    /// <param name="reportingCredit">Reporting-currency credit.</param>
    public void SetReportingAmounts(decimal reportingDebit, decimal reportingCredit)
    {
        ReportingDebit = reportingDebit;
        ReportingCredit = reportingCredit;
    }
}
