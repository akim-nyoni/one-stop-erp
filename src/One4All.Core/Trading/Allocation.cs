using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A matching of a credit open item (receipt/payment/credit note) against a debit open item (invoice) in a
/// trader ledger (<c>trd.allocation</c>, brief §4.5). Records the amount allocated in transaction and base
/// currency on each side and the realised FX gain/loss arising from their differing base rates.
/// </summary>
public sealed class Allocation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Allocation()
    {
    }

    /// <summary>Creates an allocation.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="traderId">The trader id.</param>
    /// <param name="allocationDate">The allocation date.</param>
    /// <param name="debitItemId">The debit item (invoice) id.</param>
    /// <param name="creditItemId">The credit item (receipt/credit note) id.</param>
    /// <param name="transactionAmount">The amount matched in transaction currency.</param>
    /// <param name="debitBase">The base amount drawn from the debit item.</param>
    /// <param name="creditBase">The base amount drawn from the credit item.</param>
    /// <param name="realisedFxBase">The realised FX gain (positive) or loss (negative) in base currency.</param>
    public Allocation(
        long companyId, TradingLedger ledger, long traderId, DateOnly allocationDate,
        long debitItemId, long creditItemId, decimal transactionAmount, decimal debitBase, decimal creditBase, decimal realisedFxBase)
    {
        CompanyId = companyId;
        Ledger = ledger;
        TraderId = traderId;
        AllocationDate = allocationDate;
        DebitItemId = debitItemId;
        CreditItemId = creditItemId;
        TransactionAmount = transactionAmount;
        DebitBase = debitBase;
        CreditBase = creditBase;
        RealisedFxBase = realisedFxBase;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The sub-ledger.</summary>
    public TradingLedger Ledger { get; private set; }

    /// <summary>The trader id.</summary>
    public long TraderId { get; private set; }

    /// <summary>The allocation date.</summary>
    public DateOnly AllocationDate { get; private set; }

    /// <summary>The debit item (invoice) id.</summary>
    public long DebitItemId { get; private set; }

    /// <summary>The credit item (receipt/credit note) id.</summary>
    public long CreditItemId { get; private set; }

    /// <summary>The transaction-currency amount matched.</summary>
    public decimal TransactionAmount { get; private set; }

    /// <summary>The base amount drawn from the debit item.</summary>
    public decimal DebitBase { get; private set; }

    /// <summary>The base amount drawn from the credit item.</summary>
    public decimal CreditBase { get; private set; }

    /// <summary>The realised FX gain (positive) or loss (negative) in base currency.</summary>
    public decimal RealisedFxBase { get; private set; }

    /// <summary>
    /// The amount drawn from the debit item in its own currency when the two items are in different currencies (a foreign-currency payment
    /// against a base-currency bill at the counter); null when both are in one currency and <see cref="TransactionAmount"/> is drawn from each.
    /// </summary>
    public decimal? DebitTransactionAmount { get; set; }

    /// <summary>The GL journal that posted the realised FX (if any).</summary>
    public long? JournalEntryId { get; set; }

    /// <summary>Whether this allocation has been reversed (un-allocated); retained, never deleted.</summary>
    public bool IsReversed { get; private set; }

    /// <summary>The date the allocation was reversed, if any.</summary>
    public DateOnly? ReversedOn { get; private set; }

    /// <summary>Marks the allocation reversed (brief §RCP-7).</summary>
    /// <param name="reversedOn">The reversal date.</param>
    public void Reverse(DateOnly reversedOn)
    {
        IsReversed = true;
        ReversedOn = reversedOn;
    }
}
