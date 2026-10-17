using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>The state of a supplier advance.</summary>
public enum SupplierAdvanceStatus
{
    /// <summary>Paid and not yet fully applied or refunded.</summary>
    Open = 1,

    /// <summary>Fully applied to the supplier's invoices.</summary>
    Applied = 2,

    /// <summary>The unapplied balance was refunded by the supplier.</summary>
    Refunded = 3,
}

/// <summary>
/// An advance or prepayment to a supplier (brief §4.4 "prepayments/advances to suppliers", <c>trd.supplier_advance</c>):
/// money paid before the goods or the invoice, held in an advances (prepayment) asset account rather than the
/// purchase ledger, then applied to the invoice when it arrives or refunded when the order falls through.
/// </summary>
public sealed class SupplierAdvance : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SupplierAdvance()
    {
        DocumentNumber = string.Empty;
        Currency = CurrencyCode.Parse("USD");
        AdvanceAccountCode = string.Empty;
        BankAccountCode = string.Empty;
    }

    /// <summary>Records an advance.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="supplierId">The supplier paid.</param>
    /// <param name="documentNumber">The payment document number.</param>
    /// <param name="advanceDate">The payment date.</param>
    /// <param name="currency">The currency paid.</param>
    /// <param name="exchangeRate">The rate to base currency.</param>
    /// <param name="amount">The amount paid, in the currency.</param>
    /// <param name="amountBase">The amount paid, in base currency.</param>
    /// <param name="advanceAccountCode">The advances (prepayment) asset account that holds it.</param>
    /// <param name="bankAccountCode">The bank account it was paid from.</param>
    public SupplierAdvance(long companyId, long supplierId, string documentNumber, DateOnly advanceDate, CurrencyCode currency, decimal exchangeRate, decimal amount, decimal amountBase,
        string advanceAccountCode, string bankAccountCode)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        DocumentNumber = documentNumber;
        AdvanceDate = advanceDate;
        Currency = currency;
        ExchangeRate = exchangeRate;
        Amount = amount;
        AmountBase = amountBase;
        AdvanceAccountCode = advanceAccountCode;
        BankAccountCode = bankAccountCode;
        Status = SupplierAdvanceStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The supplier paid.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The payment document number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>The payment date.</summary>
    public DateOnly AdvanceDate { get; private set; }

    /// <summary>The currency paid.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The rate to base currency.</summary>
    public decimal ExchangeRate { get; private set; }

    /// <summary>The amount paid, in the currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The amount paid, in base currency.</summary>
    public decimal AmountBase { get; private set; }

    /// <summary>How much has been applied to invoices, in the currency.</summary>
    public decimal Applied { get; private set; }

    /// <summary>How much the supplier has refunded, in the currency.</summary>
    public decimal Refunded { get; private set; }

    /// <summary>The unapplied, unrefunded balance.</summary>
    public decimal Outstanding => Amount - Applied - Refunded;

    /// <summary>The advances (prepayment) asset account that holds it.</summary>
    public string AdvanceAccountCode { get; private set; }

    /// <summary>The bank account it was paid from.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>The purchase order the advance is for, when any.</summary>
    public string? PurchaseOrderNumber { get; set; }

    /// <summary>What for.</summary>
    public string? Narrative { get; set; }

    /// <summary>The journal that paid it.</summary>
    public long? JournalEntryId { get; set; }

    /// <summary>The state of the advance.</summary>
    public SupplierAdvanceStatus Status { get; private set; }

    /// <summary>How many applications have been made (numbers the application documents).</summary>
    public int Applications { get; private set; }

    /// <summary>Applies part of the advance to an invoice.</summary>
    /// <param name="amount">The amount applied, in the currency.</param>
    /// <returns>The sequence number of this application.</returns>
    public int Apply(decimal amount)
    {
        Applied += amount;
        Applications++;
        if (Outstanding == 0m)
        {
            Status = SupplierAdvanceStatus.Applied;
        }

        return Applications;
    }

    /// <summary>Records a refund of part or all of the unapplied balance.</summary>
    /// <param name="amount">The amount refunded, in the currency.</param>
    public void Refund(decimal amount)
    {
        Refunded += amount;
        if (Outstanding == 0m)
        {
            Status = Applied > 0m && Refunded == 0m ? SupplierAdvanceStatus.Applied : SupplierAdvanceStatus.Refunded;
        }
    }
}
