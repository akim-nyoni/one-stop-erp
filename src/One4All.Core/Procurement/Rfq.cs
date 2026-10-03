using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>Where a request for quotation stands.</summary>
public enum RfqStatus
{
    /// <summary>Issued to the invited suppliers and taking quotes.</summary>
    Open = 0,

    /// <summary>Awarded to a supplier and turned into a purchase order.</summary>
    Awarded = 1,

    /// <summary>Cancelled without an award.</summary>
    Cancelled = 2,
}

/// <summary>
/// A request for quotation / tender (<c>pop.rfq</c>, brief §4.3): requirements sent to invited suppliers, their quotes
/// captured line by line with a lead time, compared and scored, and awarded to one supplier as a purchase order. The
/// award records who made it and, when it does not go to the best-scored quote, why — the adjudication trail.
/// </summary>
public sealed class Rfq : AuditableEntity, ICompanyScoped
{
    private readonly List<RfqLine> _lines = [];
    private readonly List<RfqQuote> _quotes = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Rfq()
    {
        RfqNumber = string.Empty;
        InvitedSupplierIds = [];
    }

    /// <summary>Issues a request for quotation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rfqNumber">The number.</param>
    /// <param name="issuedOn">When it was issued.</param>
    /// <param name="closesOn">The last day quotes are accepted.</param>
    /// <param name="invitedSupplierIds">The suppliers invited to quote.</param>
    public Rfq(long companyId, string rfqNumber, DateOnly issuedOn, DateOnly closesOn, IEnumerable<long> invitedSupplierIds)
    {
        CompanyId = companyId;
        RfqNumber = rfqNumber;
        IssuedOn = issuedOn;
        ClosesOn = closesOn;
        InvitedSupplierIds = [.. invitedSupplierIds.Distinct()];
        Status = RfqStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The number.</summary>
    public string RfqNumber { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The last day quotes are accepted.</summary>
    public DateOnly ClosesOn { get; private set; }

    /// <summary>The suppliers invited to quote.</summary>
    public List<long> InvitedSupplierIds { get; private set; }

    /// <summary>Where it stands.</summary>
    public RfqStatus Status { get; private set; }

    /// <summary>The supplier awarded.</summary>
    public long? AwardedSupplierId { get; private set; }

    /// <summary>The purchase order the award became.</summary>
    public long? PurchaseOrderId { get; private set; }

    /// <summary>Who made the award.</summary>
    public long? AwardedByUserId { get; private set; }

    /// <summary>Why the award did not go to the best-scored quote, when it did not.</summary>
    public string? AwardReason { get; private set; }

    /// <summary>The requirements.</summary>
    public IReadOnlyList<RfqLine> Lines => _lines.AsReadOnly();

    /// <summary>The quotes received.</summary>
    public IReadOnlyList<RfqQuote> Quotes => _quotes.AsReadOnly();

    /// <summary>Adds a requirement.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    public void AddLine(long itemId, string description, decimal quantity) => _lines.Add(new RfqLine(itemId, description, quantity));

    /// <summary>Records a supplier's quote, replacing any earlier one from them.</summary>
    /// <param name="quote">The quote.</param>
    public void RecordQuote(RfqQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        _quotes.RemoveAll(q => q.SupplierId == quote.SupplierId);
        _quotes.Add(quote);
    }

    /// <summary>Awards the request to a supplier.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="purchaseOrderId">The purchase order raised.</param>
    /// <param name="userId">Who made the award.</param>
    /// <param name="reason">Why, when not the best-scored quote.</param>
    public void Award(long supplierId, long purchaseOrderId, long userId, string? reason)
    {
        AwardedSupplierId = supplierId;
        PurchaseOrderId = purchaseOrderId;
        AwardedByUserId = userId;
        AwardReason = reason;
        Status = RfqStatus.Awarded;
    }

    /// <summary>Cancels the request.</summary>
    public void Cancel() => Status = RfqStatus.Cancelled;
}

/// <summary>A requirement on a request for quotation (<c>pop.rfq_line</c>).</summary>
public sealed class RfqLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RfqLine() => Description = string.Empty;

    /// <summary>Creates a requirement.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    public RfqLine(long itemId, string description, decimal quantity)
    {
        ItemId = itemId;
        Description = description;
        Quantity = quantity;
    }

    /// <summary>The request.</summary>
    public long RfqId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The quantity required.</summary>
    public decimal Quantity { get; private set; }
}

/// <summary>A supplier's quote (<c>pop.rfq_quote</c>): a unit price for each requirement and a lead time.</summary>
public sealed class RfqQuote : Entity
{
    private readonly List<RfqQuoteLine> _prices = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RfqQuote()
    {
    }

    /// <summary>Creates a quote.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="receivedOn">When it was received.</param>
    /// <param name="leadTimeDays">The promised lead time in days.</param>
    /// <param name="terms">Payment or delivery terms quoted, if any.</param>
    public RfqQuote(long supplierId, DateOnly receivedOn, int leadTimeDays, string? terms)
    {
        SupplierId = supplierId;
        ReceivedOn = receivedOn;
        LeadTimeDays = leadTimeDays;
        Terms = terms;
    }

    /// <summary>The request.</summary>
    public long RfqId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>When it was received.</summary>
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>The promised lead time in days.</summary>
    public int LeadTimeDays { get; private set; }

    /// <summary>The terms quoted.</summary>
    public string? Terms { get; private set; }

    /// <summary>The unit prices quoted.</summary>
    public IReadOnlyList<RfqQuoteLine> Prices => _prices.AsReadOnly();

    /// <summary>Adds a price for a requirement.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="unitPrice">The unit price.</param>
    public void AddPrice(long itemId, decimal unitPrice) => _prices.Add(new RfqQuoteLine(itemId, unitPrice));
}

/// <summary>A unit price on a quote (<c>pop.rfq_quote_line</c>).</summary>
public sealed class RfqQuoteLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RfqQuoteLine()
    {
    }

    /// <summary>Creates a price.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="unitPrice">The unit price.</param>
    public RfqQuoteLine(long itemId, decimal unitPrice)
    {
        ItemId = itemId;
        UnitPrice = unitPrice;
    }

    /// <summary>The quote.</summary>
    public long RfqQuoteId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }
}
