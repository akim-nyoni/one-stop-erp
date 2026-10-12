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
    private decimal _creditLimit;

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

    /// <summary>Gets or sets the group / head-office account this customer reports under on consolidated statements.</summary>
    public long? ParentCustomerId { get; set; }

    /// <summary>The trading currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>
    /// The credit limit in the trading currency (0 = no limit). Set when the customer is created; afterwards it can be
    /// lowered directly but raised only through an approved <see cref="CreditLimitRequest"/>.
    /// </summary>
    public decimal CreditLimit { get => _creditLimit; init => _creditLimit = value; }

    /// <summary>Lowers the credit limit, which only reduces risk and so needs no approval.</summary>
    /// <param name="limit">The new, lower limit; positive.</param>
    /// <exception cref="InvalidOperationException">Thrown when the new limit would allow more credit.</exception>
    public void LowerCreditLimit(decimal limit)
    {
        if (limit <= 0m || CreditLimitRequest.IsIncrease(_creditLimit, limit) || limit == _creditLimit)
        {
            throw new InvalidOperationException("A credit limit can be lowered directly; raising it needs an approved request.");
        }

        _creditLimit = limit;
    }

    /// <summary>Applies an approved request to raise the credit limit.</summary>
    /// <param name="request">The request; it must be this customer's and approved.</param>
    /// <exception cref="InvalidOperationException">Thrown for a request that is not this customer's or not approved.</exception>
    public void RaiseCreditLimit(CreditLimitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CustomerId != Id || request.Status != CreditLimitRequestStatus.Approved)
        {
            throw new InvalidOperationException("A credit limit is raised only by an approved request.");
        }

        _creditLimit = request.RequestedLimit;
    }

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

    /// <summary>What happens when an invoice would take the balance over the credit limit (brief §4.5 "credit limit &amp; credit-limit check policy").</summary>
    public CreditLimitPolicy CreditLimitPolicy { get; set; }

    /// <summary>
    /// Gets or sets the receivables control account this customer's items post to, when not the company's main one (brief §4.10.9: instalment
    /// debtors in a dedicated control per tenure type). Set before the customer has any items, and only for base-currency customers.
    /// </summary>
    public string? ReceivableControlAccountCode { get; set; }

    /// <summary>Whether the account is exempt from interest on arrears (government, charities, by council resolution).</summary>
    public bool InterestExempt { get; set; }

    /// <summary>Optional tax registration / VAT number.</summary>
    public string? TaxRegistration { get; set; }

    /// <summary>
    /// The individual's national ID number, when the account belongs to a person (brief §4.10.3: ownership and
    /// duplicate checks match on it). Stored upper case without spaces.
    /// </summary>
    public string? NationalIdNumber { get; set; }

    /// <summary>The email address statements and notices are sent to (brief §8).</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the customer type (retail, corporate, government…) promotions and reports select by.</summary>
    public string? CustomerType { get; set; }

    /// <summary>Gets or sets the dunning group whose timetable the customer is chased on (null: the standard stages).</summary>
    public string? DunningGroupCode { get; set; }

    /// <summary>The mobile number SMS reminders are sent to (brief §8).</summary>
    public string? MobileNumber { get; set; }

    /// <summary>Optional default tax code for this customer.</summary>
    public string? DefaultTaxCode { get; set; }

    /// <summary>Optional sales agent code.</summary>
    public string? SalesAgent { get; set; }

    /// <summary>Gets or sets the delivery zone deliveries to the customer are charged at (brief §4.13).</summary>
    public string? DeliveryZone { get; set; }

    /// <summary>The selling price list this customer buys from (null = the company's default list).</summary>
    public long? PriceListId { get; set; }

    /// <summary>The sales rep who earns commission on this customer, if any.</summary>
    public long? SalesRepId { get; set; }

    /// <summary>Whether the customer is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Computes the due date for a document from its date, using the payment terms.</summary>
    /// <param name="documentDate">The document date.</param>
    /// <returns>The due date.</returns>
    public DateOnly DueDateFor(DateOnly documentDate) => documentDate.AddDays(PaymentTermsDays);

    /// <summary>The exchange-rate type this customer's foreign documents convert at (e.g. a contract or bank rate); null: the daily rate. Falls back to the daily rate when none is found.</summary>
    public One4All.Core.System.ExchangeRateType? RateType { get; set; }
}

/// <summary>What an invoice over a customer's credit limit does.</summary>
public enum CreditLimitPolicy
{
    /// <summary>Refused unless a supervisor who may override credit limits lets it through.</summary>
    Block = 0,

    /// <summary>Posted, flagged and logged as over the limit.</summary>
    Warn = 1,

    /// <summary>Not checked (the limit is for information only).</summary>
    Ignore = 2,
}
