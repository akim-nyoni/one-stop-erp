using One4All.Application.Inventory;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>A request to add landed charges to a goods receipt.</summary>
public sealed record LandedCostRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The goods received note the charges relate to.</summary>
    public long GoodsReceivedNoteId { get; init; }

    /// <summary>The posting date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The total charges (freight, duty, clearing, insurance …).</summary>
    public decimal Amount { get; init; }

    /// <summary>How the charges are shared across the receipt's lines.</summary>
    public LandedCostBasis Basis { get; init; } = LandedCostBasis.Value;

    /// <summary>The clearing account the charges' invoice was posted to (credited as they are capitalised).</summary>
    public string ClearingAccountCode { get; init; } = string.Empty;

    /// <summary>A reference for the charges (e.g. the forwarder's invoice number); one application per reference.</summary>
    public string Reference { get; init; } = string.Empty;
}

/// <summary>One receipt line's share of the landed charges.</summary>
/// <param name="ItemCode">The item.</param>
/// <param name="Apportioned">Its share of the charges.</param>
/// <param name="Capitalised">The part added to the stock still on hand.</param>
/// <param name="ToCostOfSales">The part for stock already issued, charged to cost of sales.</param>
public readonly record struct LandedCostLineResult(string ItemCode, decimal Apportioned, decimal Capitalised, decimal ToCostOfSales);

/// <summary>The result of applying landed charges.</summary>
/// <param name="Lines">Each receipt line's share.</param>
/// <param name="JournalNumber">The journal.</param>
public sealed record LandedCostResult(IReadOnlyList<LandedCostLineResult> Lines, string JournalNumber);

/// <summary>
/// Landed costs (brief §3.6, IAS 2.11: purchase cost includes import duties, transport and handling): charges are shared
/// across a goods receipt by value or quantity, the share for stock still on hand is added to its cost (raising the
/// moving average) and the share for stock already issued goes to cost of sales.
/// </summary>
public interface ILandedCostService
{
    /// <summary>Applies landed charges to a goods receipt.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<Result<LandedCostResult>> ApplyAsync(LandedCostRequest request, CancellationToken ct = default);
}
