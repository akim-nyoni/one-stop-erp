using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A supplier (Purchase Ledger trader, brief §4.6, <c>ap.supplier</c>). Bank details are set when the supplier is
/// created; after that they change only through an approved <see cref="SupplierBankChange"/> (a dual-authorisation
/// event, since redirecting payments is a classic fraud vector). The record also holds the trading terms that drive the
/// payment run and ageing.
/// </summary>
public sealed class Supplier : SoftDeletableEntity, ICompanyScoped
{
    private string? _bankAccountName;
    private string? _bankAccountNumber;
    private string? _bankName;
    private string? _mobileMoneyNumber;

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Supplier()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a supplier.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The account reference (unique within the company).</param>
    /// <param name="name">The supplier name.</param>
    /// <param name="currency">The trading currency.</param>
    public Supplier(long companyId, string code, string name, CurrencyCode currency)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Currency = currency;
        AgeingBasis = AgeingBasis.DueDate;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The account reference.</summary>
    public string Code { get; private set; }

    /// <summary>The supplier name.</summary>
    public string Name { get; set; }

    /// <summary>The trading currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Payment terms in days.</summary>
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>How ageing is measured.</summary>
    public AgeingBasis AgeingBasis { get; set; }

    /// <summary>Whether the account is on hold (blocks payment).</summary>
    public bool OnHold { get; set; }

    /// <summary>Optional supplier category.</summary>
    public string? Category { get; set; }

    /// <summary>Optional tax registration number.</summary>
    public string? TaxRegistration { get; set; }

    /// <summary>Tax-clearance expiry date (affects withholding on payments).</summary>
    public DateOnly? TaxClearanceExpiry { get; set; }

    /// <summary>Bank account name; set at creation, then changed only by an approved bank-detail change.</summary>
    public string? BankAccountName { get => _bankAccountName; init => _bankAccountName = value; }

    /// <summary>Bank account number; set at creation, then changed only by an approved bank-detail change.</summary>
    public string? BankAccountNumber { get => _bankAccountNumber; init => _bankAccountNumber = value; }

    /// <summary>Bank name/branch; set at creation, then changed only by an approved bank-detail change.</summary>
    public string? BankName { get => _bankName; init => _bankName = value; }

    /// <summary>Mobile-money wallet paid by mobile-money payment runs; set at creation, then changed only by an approved bank-detail change.</summary>
    public string? MobileMoneyNumber { get => _mobileMoneyNumber; init => _mobileMoneyNumber = value; }

    /// <summary>Gets or sets the payment priority, 1 (paid first) to 9 (paid last): when funds are short, a payment run pays in this order.</summary>
    public int PaymentPriority { get; set; } = 5;

    /// <summary>Applies an approved bank-detail change.</summary>
    /// <param name="change">The change; it must be this supplier's and approved.</param>
    /// <exception cref="InvalidOperationException">Thrown for a change that is not this supplier's or not approved.</exception>
    public void ChangeBankDetails(SupplierBankChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.SupplierId != Id || change.Status != BankChangeStatus.Approved)
        {
            throw new InvalidOperationException("Supplier bank details change only through an approved bank-detail change.");
        }

        _bankAccountName = change.AccountName;
        _bankAccountNumber = change.AccountNumber;
        _bankName = change.BankName;
        if (change.MobileMoneyNumber is not null)
        {
            _mobileMoneyNumber = change.MobileMoneyNumber.Length == 0 ? null : change.MobileMoneyNumber;
        }
    }

    /// <summary>Whether the supplier is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the supplier is blacklisted (brief §4.3): no new orders, requisitions or invitations to quote. What is
    /// already owed can still be settled.
    /// </summary>
    public bool IsBlacklisted { get; private set; }

    /// <summary>Why the supplier was blacklisted.</summary>
    public string? BlacklistReason { get; private set; }

    /// <summary>Blacklists the supplier.</summary>
    /// <param name="reason">Why; recorded for the audit trail.</param>
    public void Blacklist(string reason)
    {
        IsBlacklisted = true;
        BlacklistReason = reason;
    }

    /// <summary>Takes the supplier off the blacklist.</summary>
    public void Reinstate()
    {
        IsBlacklisted = false;
        BlacklistReason = null;
    }

    /// <summary>Computes the due date for a document from its date, using the payment terms.</summary>
    /// <param name="documentDate">The document date.</param>
    /// <returns>The due date.</returns>
    public DateOnly DueDateFor(DateOnly documentDate) => documentDate.AddDays(PaymentTermsDays);

    /// <summary>The exchange-rate type this supplier's foreign documents convert at (e.g. a contract or bank rate); null: the daily rate. Falls back to the daily rate when none is found.</summary>
    public One4All.Core.System.ExchangeRateType? RateType { get; set; }

    /// <summary>This supplier's over-receipt tolerance (percent), when it differs from the company's.</summary>
    public decimal? OverReceiptTolerancePercent { get; set; }
}
