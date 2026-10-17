using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>What happened at the till (brief §14 F: voids, refunds, discounts, overrides, no-sale, parking, loyalty, reprints, fiscal).</summary>
public enum PosEventKind
{
    /// <summary>A line removed from a sale before completion.</summary>
    LineVoid = 1,

    /// <summary>A completed sale voided.</summary>
    SaleVoid = 2,

    /// <summary>Goods refunded against a completed sale.</summary>
    Refund = 3,

    /// <summary>A discount given on a line.</summary>
    LineDiscount = 4,

    /// <summary>A price keyed over the list price.</summary>
    PriceOverride = 5,

    /// <summary>The drawer opened without a sale.</summary>
    NoSale = 6,

    /// <summary>A sale parked (suspended) at the till.</summary>
    ParkedSale = 7,

    /// <summary>A parked sale resumed.</summary>
    ResumedSale = 8,

    /// <summary>A receipt reprinted.</summary>
    Reprint = 9,

    /// <summary>A sale submitted to the fiscal device / revenue authority.</summary>
    FiscalSubmission = 10,

    /// <summary>Loyalty points earned on a sale.</summary>
    LoyaltyEarned = 11,

    /// <summary>Loyalty points redeemed against a sale.</summary>
    LoyaltyRedeemed = 12,

    /// <summary>The till passed to another cashier mid-shift without a cash-up (the drawer stays shared until the hand-over or Z-read).</summary>
    CashierSwitch = 13,
}

/// <summary>
/// One recorded till event (<c>pos.event</c>). Append-only: the audit trail behind the void, refund, discount, override,
/// no-sale, parked-sale, loyalty, reprint and fiscal-submission reports.
/// </summary>
public sealed class PosEvent : Entity, ICompanyScoped
{
    private PosEvent()
    {
        Description = string.Empty;
    }

    /// <summary>Records an event.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="occurredOnUtc">When.</param>
    /// <param name="eventDate">The trading date.</param>
    /// <param name="description">What it was about (item, sale or action).</param>
    /// <param name="amount">The value involved (discount, refund, override price, points…).</param>
    public PosEvent(long companyId, long terminalId, long cashierUserId, PosEventKind kind, DateTimeOffset occurredOnUtc, DateOnly eventDate, string description, decimal amount)
    {
        CompanyId = companyId;
        TerminalId = terminalId;
        CashierUserId = cashierUserId;
        Kind = kind;
        OccurredOnUtc = occurredOnUtc;
        EventDate = eventDate;
        Description = description;
        Amount = amount;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the till.</summary>
    public long TerminalId { get; private set; }

    /// <summary>Gets or sets the till session, when one was open.</summary>
    public long? TillSessionId { get; set; }

    /// <summary>Gets the cashier.</summary>
    public long CashierUserId { get; private set; }

    /// <summary>Gets what happened.</summary>
    public PosEventKind Kind { get; private set; }

    /// <summary>Gets when it happened.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>Gets the trading date.</summary>
    public DateOnly EventDate { get; private set; }

    /// <summary>Gets or sets the sale number it relates to.</summary>
    public string? SaleNumber { get; set; }

    /// <summary>Gets or sets the item it relates to.</summary>
    public long? ItemId { get; set; }

    /// <summary>Gets or sets the sale line it relates to (refunds).</summary>
    public long? SaleLineId { get; set; }

    /// <summary>Gets or sets the quantity involved (units refunded, voided or discounted).</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Gets what it was about.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the value involved.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets or sets the value before the event (list price for an override or discount).</summary>
    public decimal? OriginalAmount { get; set; }

    /// <summary>Gets or sets the reason given.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets the supervisor who authorised it.</summary>
    public long? AuthorisedByUserId { get; set; }

    /// <summary>Gets or sets the loyalty customer reference (card or phone number).</summary>
    public string? CustomerReference { get; set; }

    /// <summary>Gets or sets a reference produced by the event (refund journal, fiscal receipt number).</summary>
    public string? Reference { get; set; }

    /// <summary>Gets or sets the outcome, for fiscal submissions (Accepted, Rejected: message, Pending).</summary>
    public string? Outcome { get; set; }
}
