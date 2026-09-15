using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A customer (Sales Ledger trader, brief §4.5, <c>ar.customer</c>). Carries the trading terms that drive
/// invoicing, ageing, credit control and statements. The AR control account in the GL is company-level;
/// this record is the sub-ledger identity that open items hang off.
/// </summary>
public sealed class Customer : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Customer()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a customer.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The account reference (unique within the company).</param>
    /// <param name="name">The customer name.</param>
    /// <param name="currency">The trading currency.</param>
    public Customer(long companyId, string code, string name, CurrencyCode currency)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Currency = currency;
        AgeingBasis = AgeingBasis.DueDate;
        StatementType = StatementType.OpenItem;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The account reference.</summary>
    public string Code { get; private set; }

    /// <summary>The customer name.</summary>
    public string Name { get; set; }

    /// <summary>The trading currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The credit limit in the trading currency (0 = no credit).</summary>
    public decimal CreditLimit { get; set; }

    /// <summary>Payment terms in days from the ageing-basis date.</summary>
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>Settlement (early-payment) discount percentage.</summary>
    public decimal SettlementDiscountPercent { get; set; }

    /// <summary>How ageing is measured.</summary>
    public AgeingBasis AgeingBasis { get; set; }

    /// <summary>The statement style.</summary>
    public StatementType StatementType { get; set; }

    /// <summary>Whether the account is on hold (blocks new sales on credit).</summary>
    public bool OnHold { get; set; }

    /// <summary>Optional tax registration / VAT number.</summary>
    public string? TaxRegistration { get; set; }

    /// <summary>Optional default tax code for this customer.</summary>
    public string? DefaultTaxCode { get; set; }

    /// <summary>Optional sales agent code.</summary>
    public string? SalesAgent { get; set; }

    /// <summary>Whether the customer is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Computes the due date for a document from its date, using the payment terms.</summary>
    /// <param name="documentDate">The document date.</param>
    /// <returns>The due date.</returns>
    public DateOnly DueDateFor(DateOnly documentDate) => documentDate.AddDays(PaymentTermsDays);
}
