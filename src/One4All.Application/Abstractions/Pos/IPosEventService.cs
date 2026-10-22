using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A till event to record.</summary>
public sealed class PosEventRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the till.</summary>
    public long TerminalId { get; init; }

    /// <summary>Gets the till session, when one is open.</summary>
    public long? TillSessionId { get; init; }

    /// <summary>Gets the cashier.</summary>
    public long CashierUserId { get; init; }

    /// <summary>Gets what happened.</summary>
    public PosEventKind Kind { get; init; }

    /// <summary>Gets the trading date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets what it was about.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the value involved (discount, override price, points, receipt value).</summary>
    public decimal Amount { get; init; }

    /// <summary>Gets the value before the event (list amount), for discounts and overrides.</summary>
    public decimal? OriginalAmount { get; init; }

    /// <summary>Gets the sale number.</summary>
    public string? SaleNumber { get; init; }

    /// <summary>Gets the item.</summary>
    public long? ItemId { get; init; }

    /// <summary>Gets the reason.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the authorising supervisor.</summary>
    public long? AuthorisedByUserId { get; init; }

    /// <summary>Gets the loyalty customer reference.</summary>
    public string? CustomerReference { get; init; }

    /// <summary>Gets the reference (fiscal receipt number).</summary>
    public string? Reference { get; init; }

    /// <summary>Gets the outcome, for fiscal submissions.</summary>
    public string? Outcome { get; init; }
}

/// <summary>One sale line being refunded.</summary>
/// <param name="SaleLineId">The sale line.</param>
/// <param name="Quantity">How many units come back.</param>
public readonly record struct PosRefundLine(long SaleLineId, decimal Quantity);

/// <summary>A refund against a completed sale.</summary>
public sealed class PosRefundRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the original sale.</summary>
    public long SaleId { get; init; }

    /// <summary>Gets the till paying the refund.</summary>
    public long TerminalId { get; init; }

    /// <summary>Gets the cashier paying it.</summary>
    public long CashierUserId { get; init; }

    /// <summary>Gets the supervisor authorising it.</summary>
    public long? AuthorisedByUserId { get; init; }

    /// <summary>Gets the refund date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>
    /// Gets the account the money leaves from when it is paid in cash or by <see cref="RefundTender"/>: the till's cash account for the cash part
    /// of a refund through the original tenders (blank: the sale's own cash account), or the account of the other method chosen.
    /// </summary>
    public string RefundAccountCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the method the refund is paid by when not through the sale's own tenders (null, the default: each tender gets its share back - cash
    /// to the drawer, card to the card's account, on account as a credit to the customer, gift card back onto the card). Paying by a method the
    /// sale was not paid by (cash for a card sale) needs an authoriser with the third-level refund authority (Pos.Refund, AuthoriseLevel3).
    /// </summary>
    public TenderType? RefundTender { get; init; }

    /// <summary>Gets the output-tax account, needed when the refunded lines carried tax.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>Gets the reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets the lines and quantities coming back.</summary>
    public IReadOnlyList<PosRefundLine> Lines { get; init; } = [];

    /// <summary>Gets a value indicating whether the goods go back on the shelf (reversing cost of sales at the original cost).</summary>
    public bool Restock { get; init; }

    /// <summary>Gets the open till session of this till the cash is paid out of; required whenever any of the refund is paid in cash.</summary>
    public long? TillSessionId { get; init; }
}

/// <summary>
/// An exchange at the till: goods from an earlier sale come back and new goods go out in one unit; the returned value pays towards the new
/// sale and only the difference changes hands.
/// </summary>
public sealed class PosExchangeRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the original sale.</summary>
    public long OriginalSaleId { get; init; }

    /// <summary>Gets the till.</summary>
    public long TerminalId { get; init; }

    /// <summary>Gets the open till session.</summary>
    public long TillSessionId { get; init; }

    /// <summary>Gets the cashier.</summary>
    public long CashierUserId { get; init; }

    /// <summary>Gets the supervisor authorising the return.</summary>
    public long? AuthorisedByUserId { get; init; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets the lines coming back.</summary>
    public IReadOnlyList<PosRefundLine> Returns { get; init; } = [];

    /// <summary>Gets a value indicating whether the returned goods go back on the shelf.</summary>
    public bool Restock { get; init; } = true;

    /// <summary>Gets the till's cash account (the returned value passes through it).</summary>
    public string CashAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the output-tax account.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>Gets the new sale's number.</summary>
    public string NewSaleNumber { get; init; } = string.Empty;

    /// <summary>Gets the warehouse the new goods leave from.</summary>
    public long WarehouseId { get; init; }

    /// <summary>Gets the new goods.</summary>
    public IReadOnlyList<PosSaleLineRequest> NewLines { get; init; } = [];

    /// <summary>Gets the tenders paying any balance (none: the balance is paid in cash; ignored when the returned value covers the new goods: the rest is given back as cash).</summary>
    public IReadOnlyList<PosTenderRequest> BalanceTenders { get; init; } = [];
}

/// <summary>A completed exchange.</summary>
/// <param name="RefundJournalNumber">The return's journal.</param>
/// <param name="Returned">The value returned (tax included).</param>
/// <param name="NewSaleId">The new sale.</param>
/// <param name="NewSaleGross">The new sale's value.</param>
/// <param name="Difference">What the customer paid (+) or was given back (−).</param>
public readonly record struct PosExchangeResult(string RefundJournalNumber, decimal Returned, long NewSaleId, decimal NewSaleGross, decimal Difference);

/// <summary>A refund without a receipt.</summary>
public sealed class UnreceiptedRefundRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the till.</summary>
    public long TerminalId { get; init; }

    /// <summary>Gets the cashier.</summary>
    public long CashierUserId { get; init; }

    /// <summary>Gets the supervisor authorising it (required; not the cashier).</summary>
    public long? AuthorisedByUserId { get; init; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the item returned.</summary>
    public long ItemId { get; init; }

    /// <summary>Gets the quantity.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Gets the unit price refunded (tax-exclusive).</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Gets the tax refunded.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>Gets the output tax account (with tax).</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>Gets the account the money is paid from.</summary>
    public string RefundAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets the warehouse the goods go back into (null: not restocked).</summary>
    public long? RestockWarehouseId { get; init; }

    /// <summary>Gets the open till session the cash is paid out of (null when not paid from a drawer).</summary>
    public long? TillSessionId { get; init; }
}

/// <summary>The posted refund.</summary>
/// <param name="EventId">The recorded refund event.</param>
/// <param name="JournalNumber">The refund journal.</param>
/// <param name="Gross">The amount paid back, tax included.</param>
public readonly record struct PosRefundResult(long EventId, string JournalNumber, decimal Gross);

/// <summary>Records till events and posts refunds (brief §14 F).</summary>
public interface IPosEventService
{
    /// <summary>Records a till event after checking it carries what its kind needs.</summary>
    /// <param name="request">The event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The event id, or an error.</returns>
    Task<Result<long>> RecordAsync(PosEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Refunds lines of a completed sale: Dr revenue and output tax pro rata to the quantity, Cr the refund account; when
    /// restocking, the goods come back into stock at the cost they left at (Dr stock, Cr cost of sales). Refunds cannot exceed what was sold.
    /// </summary>
    /// <param name="request">The refund.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refund, or an error.</returns>
    Task<Result<PosRefundResult>> RefundAsync(PosRefundRequest request, CancellationToken ct = default);

    /// <summary>
    /// Refunds goods brought back without a receipt: always authorised by a supervisor other than the cashier, never above the company's
    /// limit for such refunds (<c>Pos.UnreceiptedNotAllowed</c> / <c>Pos.UnreceiptedLimit</c>); revenue (and tax) reversed against the
    /// item's revenue account and the goods optionally put back into stock at their average cost.
    /// </summary>
    /// <param name="request">The refund.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refund.</returns>
    Task<Result<PosRefundResult>> RefundWithoutReceiptAsync(UnreceiptedRefundRequest request, CancellationToken ct = default);

    /// <summary>
    /// Exchanges goods: the return is refunded through the till's cash account and the same value tenders the new sale, all in one unit;
    /// the customer pays any balance with the given tenders, or is given the excess back in cash. The drawer moves only by the difference.
    /// </summary>
    /// <param name="request">The exchange.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exchange.</returns>
    Task<Result<PosExchangeResult>> ExchangeAsync(PosExchangeRequest request, CancellationToken ct = default);
}
