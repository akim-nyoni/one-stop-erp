using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>The lifecycle of a sales quotation.</summary>
public enum QuotationStatus
{
    /// <summary>Being prepared.</summary>
    Draft = 1,

    /// <summary>Sent to the customer.</summary>
    Sent = 2,

    /// <summary>Accepted by the customer.</summary>
    Accepted = 3,

    /// <summary>Declined by the customer.</summary>
    Declined = 4,

    /// <summary>Converted into a sales order.</summary>
    Converted = 5,
}

/// <summary>
/// A sales quotation (<c>sop.quotation</c>): a priced offer to a customer that precedes an order. It touches
/// no ledger; once accepted it can be converted into a sales order, which then drives despatch and invoicing.
/// </summary>
public sealed class Quotation : AuditableEntity, ICompanyScoped
{
    private readonly List<QuotationLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Quotation() => QuoteNumber = string.Empty;

    /// <summary>Creates a draft quotation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="quoteNumber">The quotation number.</param>
    /// <param name="quoteDate">The quotation date.</param>
    /// <param name="validUntil">The date the quote lapses.</param>
    /// <param name="currency">The quotation currency.</param>
    public Quotation(long companyId, long customerId, string quoteNumber, DateOnly quoteDate, DateOnly validUntil, CurrencyCode currency)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        QuoteNumber = quoteNumber;
        QuoteDate = quoteDate;
        ValidUntil = validUntil;
        Currency = currency;
        Status = QuotationStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The quotation number.</summary>
    public string QuoteNumber { get; private set; }

    /// <summary>The quotation date.</summary>
    public DateOnly QuoteDate { get; private set; }

    /// <summary>The date the quote lapses.</summary>
    public DateOnly ValidUntil { get; private set; }

    /// <summary>The quotation currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The status.</summary>
    public QuotationStatus Status { get; private set; }

    /// <summary>The sales order this quote became, once converted.</summary>
    public long? SalesOrderId { get; private set; }

    /// <summary>The quotation lines.</summary>
    public IReadOnlyList<QuotationLine> Lines => _lines.AsReadOnly();

    /// <summary>The quotation total (sum of line values, before tax).</summary>
    public decimal Total => _lines.Sum(l => l.Quantity * l.UnitPrice);

    /// <summary>Adds a line.</summary>
    /// <param name="itemId">The stock item.</param>
    /// <param name="description">The line description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxCode">The tax code, if any.</param>
    /// <param name="revenueAccountCode">The revenue account for the eventual order.</param>
    public QuotationLine AddLine(long itemId, string description, decimal quantity, decimal unitPrice, string? taxCode, string revenueAccountCode)
    {
        QuotationLine line = new(itemId, description, quantity, unitPrice, taxCode, revenueAccountCode);
        _lines.Add(line);
        return line;
    }

    /// <summary>Marks the quotation sent.</summary>
    public void Send() => Status = QuotationStatus.Sent;

    /// <summary>Marks the quotation accepted.</summary>
    public void Accept() => Status = QuotationStatus.Accepted;

    /// <summary>Marks the quotation declined.</summary>
    public void Decline() => Status = QuotationStatus.Declined;

    /// <summary>Marks the quotation converted, recording the resulting order.</summary>
    /// <param name="salesOrderId">The created sales order.</param>
    public void MarkConverted(long salesOrderId)
    {
        Status = QuotationStatus.Converted;
        SalesOrderId = salesOrderId;
    }
}

/// <summary>One line of a sales quotation (<c>sop.quotation_line</c>).</summary>
public sealed class QuotationLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private QuotationLine()
    {
        Description = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a quotation line.</summary>
    /// <param name="itemId">The stock item.</param>
    /// <param name="description">The line description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxCode">The tax code, if any.</param>
    /// <param name="revenueAccountCode">The revenue account for the eventual order.</param>
    public QuotationLine(long itemId, string description, decimal quantity, decimal unitPrice, string? taxCode, string revenueAccountCode)
    {
        ItemId = itemId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TaxCode = taxCode;
        RevenueAccountCode = revenueAccountCode;
    }

    /// <summary>The owning quotation id.</summary>
    public long QuotationId { get; private set; }

    /// <summary>The stock item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The line description.</summary>
    public string Description { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The tax code, if any.</summary>
    public string? TaxCode { get; private set; }

    /// <summary>The revenue account for the eventual order.</summary>
    public string RevenueAccountCode { get; private set; }
}
