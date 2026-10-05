using One4All.Core.Common;
using One4All.Core.JobCosting;

namespace One4All.Application.Abstractions.JobCosting;

/// <summary>The first weigh that opens a ticket.</summary>
public sealed class FirstWeighRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets whether the load arrives or leaves.</summary>
    public WeighDirection Direction { get; init; }

    /// <summary>Gets the vehicle registration.</summary>
    public string Vehicle { get; init; } = string.Empty;

    /// <summary>Gets the product item.</summary>
    public long ItemId { get; init; }

    /// <summary>Gets the m³ per tonne for the product (bulk density inverse).</summary>
    public decimal CubicMetresPerTonne { get; init; }

    /// <summary>Gets the customer, for deliveries.</summary>
    public long? CustomerId { get; init; }

    /// <summary>Gets the supplier, for receipts.</summary>
    public long? SupplierId { get; init; }

    /// <summary>Gets the driver.</summary>
    public string? Driver { get; init; }

    /// <summary>Gets the delivery site.</summary>
    public string? Site { get; init; }

    /// <summary>Gets the stockpile the load is drawn from (outbound) — its stock is issued when the ticket completes.</summary>
    public long? WarehouseId { get; init; }

    /// <summary>Gets the haulage distance.</summary>
    public decimal? DistanceKm { get; init; }

    /// <summary>Gets the concrete batch run the load came from.</summary>
    public string? BatchRunNumber { get; init; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the weight, or null to use the vehicle's stored tare (outbound only).</summary>
    public decimal? WeightKg { get; init; }

    /// <summary>Gets the operator.</summary>
    public long? OperatorUserId { get; init; }

    /// <summary>Gets a value indicating whether the weight was keyed by hand.</summary>
    public bool IsManual { get; init; }

    /// <summary>Gets why it was keyed.</summary>
    public string? ManualReason { get; init; }

    /// <summary>Gets the supervisor authorising a manual weight.</summary>
    public long? AuthorisedByUserId { get; init; }
}

/// <summary>The second weigh that completes a ticket.</summary>
public sealed class SecondWeighRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the ticket.</summary>
    public long TicketId { get; init; }

    /// <summary>Gets the weight.</summary>
    public decimal WeightKg { get; init; }

    /// <summary>Gets the moisture / contamination deduction.</summary>
    public decimal DeductionKg { get; init; }

    /// <summary>Gets the operator.</summary>
    public long? OperatorUserId { get; init; }

    /// <summary>Gets a value indicating whether the weight was keyed by hand.</summary>
    public bool IsManual { get; init; }

    /// <summary>Gets why it was keyed.</summary>
    public string? ManualReason { get; init; }

    /// <summary>Gets the supervisor authorising a manual weight.</summary>
    public long? AuthorisedByUserId { get; init; }
}

/// <summary>The completed ticket.</summary>
/// <param name="NetTonnes">The net tonnes.</param>
/// <param name="NetCubicMetres">The net m³.</param>
/// <param name="HaulageCharge">The haulage charged.</param>
/// <param name="Overloaded">Whether the gross exceeded the vehicle's legal maximum.</param>
public readonly record struct WeighbridgeCompletion(decimal NetTonnes, decimal NetCubicMetres, decimal HaulageCharge, bool Overloaded);

/// <summary>Weighbridge operations (brief §4.x and §14 L): two weighs per ticket, manual-weight control, haulage, stockpile issues.</summary>
public interface IWeighbridgeService
{
    /// <summary>Opens a WB-numbered ticket with the first weight and the time in.</summary>
    /// <param name="request">The first weigh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ticket id and number, or an error.</returns>
    Task<Result<(long TicketId, string TicketNumber)>> FirstWeighAsync(FirstWeighRequest request, CancellationToken ct = default);

    /// <summary>
    /// Completes a ticket with the second weight and the time out, charges haulage by zone, and for an outbound load from a stockpile
    /// issues the net tonnes from stock — all together or not at all.
    /// </summary>
    /// <param name="request">The second weigh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The completion, or an error.</returns>
    Task<Result<WeighbridgeCompletion>> SecondWeighAsync(SecondWeighRequest request, CancellationToken ct = default);
}
