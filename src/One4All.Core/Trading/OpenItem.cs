using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Which sub-ledger an open item belongs to.</summary>
public enum TradingLedger
{
    /// <summary>Accounts Receivable (customers).</summary>
    AccountsReceivable = 1,

    /// <summary>Accounts Payable (suppliers).</summary>
    AccountsPayable = 2,
}

/// <summary>
/// An open item in a customer or supplier ledger (<c>trd.open_item</c>, brief §4.5/§4.6). Every invoice,
/// credit note, receipt and payment is an open item that carries its outstanding amount in both the
/// transaction currency and base currency. Allocation matches debit items against credit items and
/// realises FX from the base-rate difference. Amounts are stored non-negative; direction is in <see cref="Sign"/>.
/// </summary>
public sealed class OpenItem : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private OpenItem()
    {
        DocumentNumber = string.Empty;
    }

    /// <summary>Creates an open item.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="traderId">The customer or supplier id.</param>
    /// <param name="itemType">The document type.</param>
    /// <param name="sign">The ledger sign (debit/credit).</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="documentDate">The document date.</param>
    /// <param name="dueDate">The due date.</param>
    /// <param name="currency">The transaction currency.</param>
    /// <param name="exchangeRate">The rate to base at document date.</param>
    /// <param name="amountTransaction">The gross amount in transaction currency (non-negative).</param>
    /// <param name="amountBase">The gross amount in base currency (non-negative).</param>
    public OpenItem(
        long companyId, TradingLedger ledger, long traderId, OpenItemType itemType, LedgerSign sign,
        string documentNumber, DateOnly documentDate, DateOnly dueDate, CurrencyCode currency,
        decimal exchangeRate, decimal amountTransaction, decimal amountBase)
    {
        CompanyId = companyId;
        Ledger = ledger;
        TraderId = traderId;
        ItemType = itemType;
        Sign = sign;
        DocumentNumber = documentNumber;
        DocumentDate = documentDate;
        DueDate = dueDate;
        Currency = currency;
        ExchangeRate = exchangeRate;
        OriginalTransaction = amountTransaction;
        OriginalBase = amountBase;
        OutstandingTransaction = amountTransaction;
        OutstandingBase = amountBase;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The sub-ledger.</summary>
    public TradingLedger Ledger { get; private set; }

    /// <summary>The customer or supplier id.</summary>
    public long TraderId { get; private set; }

    /// <summary>The document type.</summary>
    public OpenItemType ItemType { get; private set; }

    /// <summary>The ledger sign (debit or credit).</summary>
    public LedgerSign Sign { get; private set; }

    /// <summary>The document number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>The document date.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>The due date.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>The fiscal (e-invoicing) code the authority gave this invoice or credit note, or null.</summary>
    public string? FiscalCode { get; set; }

    /// <summary>The verification URL or signature on a signed invoice or credit note.</summary>
    public string? FiscalVerification { get; set; }

    /// <summary>Whether the invoice or credit note still waits for the fiscal device.</summary>
    public bool FiscalQueued { get; set; }

    /// <summary>The transaction currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The rate to base at document date.</summary>
    public decimal ExchangeRate { get; private set; }

    /// <summary>The original gross amount in transaction currency.</summary>
    public decimal OriginalTransaction { get; private set; }

    /// <summary>The original gross amount in base currency.</summary>
    public decimal OriginalBase { get; private set; }

    /// <summary>The amount still outstanding in transaction currency.</summary>
    public decimal OutstandingTransaction { get; private set; }

    /// <summary>The amount still outstanding in base currency.</summary>
    public decimal OutstandingBase { get; private set; }

    /// <summary>The GL journal that posted this item.</summary>
    public long? JournalEntryId { get; set; }

    /// <summary>Optional narrative.</summary>
    public string? Narrative { get; set; }

    /// <summary>Gets or sets the tax point when it differs from the document date (a delivery before or long after the invoice).</summary>
    public DateOnly? TaxPointDate { get; set; }

    /// <summary>Gets or sets the reason code a credit note was raised under.</summary>
    public string? ReasonCode { get; set; }

    /// <summary>Gets or sets the invoice a credit note reverses (the reference the fiscal device needs), or null.</summary>
    public string? ReferenceDocumentNumber { get; set; }

    /// <summary>
    /// The date finance charges on this item have been raised up to, or null when none have. The next run
    /// charges only the days after it, so a period is never charged twice.
    /// </summary>
    public DateOnly? InterestChargedTo { get; private set; }

    /// <summary>True when fully allocated/settled (nothing outstanding in transaction currency).</summary>
    public bool IsSettled => OutstandingTransaction == 0m;

    /// <summary>Records that finance charges have been raised on this item up to a date (never moves backwards).</summary>
    /// <param name="date">The run date.</param>
    public void RecordInterestChargedTo(DateOnly date)
    {
        if (InterestChargedTo is null || date > InterestChargedTo)
        {
            InterestChargedTo = date;
        }
    }

    /// <summary>
    /// Reclassifies a receivable invoice as a finance (interest) charge, so it is identifiable on statements and
    /// is not itself charged interest. Only an AR debit invoice can be reclassified.
    /// </summary>
    public void MarkAsInterestCharge()
    {
        if (Ledger != TradingLedger.AccountsReceivable || Sign != LedgerSign.Debit || ItemType != OpenItemType.Invoice)
        {
            throw new InvalidOperationException($"{DocumentNumber} is not a receivable invoice and cannot be marked as a finance charge.");
        }

        ItemType = OpenItemType.Interest;
    }

    /// <summary>
    /// Reduces the outstanding amounts by an allocation. The base reduction is supplied explicitly so the
    /// caller controls how the item's base value is drawn down (its original rate), which is what makes the
    /// FX difference on the counter-item a realised gain/loss.
    /// </summary>
    /// <param name="transactionAmount">The transaction-currency amount allocated.</param>
    /// <param name="baseAmount">The base-currency amount to reduce.</param>
    /// <exception cref="InvalidOperationException">Thrown when the allocation exceeds the outstanding amount.</exception>
    public void ReduceOutstanding(decimal transactionAmount, decimal baseAmount)
    {
        if (transactionAmount > OutstandingTransaction + 0.0001m)
        {
            throw new InvalidOperationException(
                $"Allocation {transactionAmount} exceeds outstanding {OutstandingTransaction} on {DocumentNumber}.");
        }

        OutstandingTransaction = decimal.Round(OutstandingTransaction - transactionAmount, 4, MidpointRounding.ToEven);
        OutstandingBase = decimal.Round(OutstandingBase - baseAmount, 4, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Restores outstanding amounts when an allocation is reversed (un-allocation, brief §RCP-7). The base
    /// restoration mirrors the base amount that was originally drawn, so reversing an allocation returns the
    /// item to exactly its pre-allocation state. Cannot restore more than the item's original amount.
    /// </summary>
    /// <param name="transactionAmount">The transaction-currency amount to restore.</param>
    /// <param name="baseAmount">The base-currency amount to restore.</param>
    /// <exception cref="InvalidOperationException">Thrown when the restoration exceeds the original amount.</exception>
    public void RestoreOutstanding(decimal transactionAmount, decimal baseAmount)
    {
        if (OutstandingTransaction + transactionAmount > OriginalTransaction + 0.0001m)
        {
            throw new InvalidOperationException(
                $"Restoring {transactionAmount} would exceed the original {OriginalTransaction} on {DocumentNumber}.");
        }

        OutstandingTransaction = decimal.Round(OutstandingTransaction + transactionAmount, 4, MidpointRounding.ToEven);
        OutstandingBase = decimal.Round(OutstandingBase + baseAmount, 4, MidpointRounding.ToEven);
    }
}
