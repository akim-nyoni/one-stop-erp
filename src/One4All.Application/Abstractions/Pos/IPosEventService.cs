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

    /// <summary>Gets the account the money leaves from (till cash, card clearing).</summary>
    public string RefundAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the output-tax account, needed when the refunded lines carried tax.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>Gets the reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets the lines and quantities coming back.</summary>
    public IReadOnlyList<PosRefundLine> Lines { get; init; } = [];

    /// <summary>Gets a value indicating whether the goods go back on the shelf (reversing cost of sales at the original cost).</summary>
    public bool Restock { get; init; }
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
}
