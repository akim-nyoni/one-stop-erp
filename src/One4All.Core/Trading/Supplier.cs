using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A supplier (Purchase Ledger trader, brief §4.6, <c>ap.supplier</c>). Bank-detail changes are a
/// dual-authorisation event elsewhere (a classic fraud vector); this record holds the current details and
/// the trading terms that drive the payment run and ageing.
/// </summary>
public sealed class Supplier : SoftDeletableEntity, ICompanyScoped
{
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

    /// <summary>Bank account name.</summary>
    public string? BankAccountName { get; set; }

    /// <summary>Bank account number.</summary>
    public string? BankAccountNumber { get; set; }

    /// <summary>Bank name/branch.</summary>
    public string? BankName { get; set; }

    /// <summary>Whether the supplier is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Computes the due date for a document from its date, using the payment terms.</summary>
    /// <param name="documentDate">The document date.</param>
    /// <returns>The due date.</returns>
    public DateOnly DueDateFor(DateOnly documentDate) => documentDate.AddDays(PaymentTermsDays);
}
