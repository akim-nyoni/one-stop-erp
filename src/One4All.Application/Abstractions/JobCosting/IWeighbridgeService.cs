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

    /// <summary>Gets the stockpile the load is drawn from (outbound) - its stock is issued when the ticket completes.</summary>
    public long? WarehouseId { get; init; }

    /// <summary>Gets the haulage distance.</summary>
    public decimal? DistanceKm { get; init; }

    /// <summary>Gets the concrete batch run the load came from.</summary>
    public string? BatchRunNumber { get; init; }

    /// <summary>Gets the sales order the load is delivered against (outbound; open, for the same customer, with a line for the product).</summary>
    public string? SalesOrderNumber { get; init; }

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
/// <summary>Bills a customer for the loads weighed out to them (brief §4.14: tickets flow to delivery notes and invoices).</summary>
public sealed class WeighbridgeInvoiceRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the customer billed.</summary>
    public long CustomerId { get; init; }

    /// <summary>Gets the invoice number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>Gets the invoice date; completed outbound tickets dated on or before it and not yet invoiced are billed.</summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>Gets the price per net tonne of product.</summary>
    public decimal PricePerTonne { get; init; }

    /// <summary>Gets the revenue account for the product.</summary>
    public string SalesAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the revenue account for haulage (defaults to the sales account).</summary>
    public string? HaulageAccountCode { get; init; }

    /// <summary>Gets the tax code charged on top (null: no tax).</summary>
    public string? TaxCode { get; init; }

    /// <summary>Gets the user raising the invoice.</summary>
    public long? EnteredByUserId { get; init; }
}

/// <summary>An invoice raised from weighbridge tickets.</summary>
/// <param name="Tickets">The tickets billed.</param>
/// <param name="Tonnes">The net tonnes billed.</param>
/// <param name="Product">The product value.</param>
/// <param name="Haulage">The haulage charged.</param>
/// <param name="Tax">The tax.</param>
public readonly record struct WeighbridgeInvoice(IReadOnlyList<string> Tickets, decimal Tonnes, decimal Product, decimal Haulage, decimal Tax)
{
    /// <summary>The invoice total.</summary>
    public decimal Total => Product + Haulage + Tax;
}

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
    /// issues the net tonnes from stock - all together or not at all.
    /// </summary>
    /// <param name="request">The second weigh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The completion, or an error.</returns>
    Task<Result<WeighbridgeCompletion>> SecondWeighAsync(SecondWeighRequest request, CancellationToken ct = default);

    /// <summary>Invoices a customer for their completed, un-invoiced outbound loads: net tonnes at the price plus each load's haulage, with tax on top.</summary>
    /// <param name="request">The invoice.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was billed, or nothing to bill, an unknown tax code or a posting failure.</returns>
    Task<Result<WeighbridgeInvoice>> InvoiceTicketsAsync(WeighbridgeInvoiceRequest request, CancellationToken ct = default);

    /// <summary>
    /// The printed weighbridge ticket (brief §6 report 198): vehicle, driver, product, customer or supplier, order, site, gross, tare, deduction and net
    /// weights with the volume, times in and out, a keyed weight flagged, and lines for the driver's and weighbridge clerk's signatures.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ticketNumber">The ticket.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ticket text, or why there is none.</returns>
    Task<Result<string>> RenderTicketAsync(long companyId, string ticketNumber, CancellationToken ct = default);

    /// <summary>Registers a vehicle or changes its details (brief §4.2 vehicle register): haulier, stored tare, the gross it may not exceed, and whether it may use the bridge.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="registration">The registration.</param>
    /// <param name="haulier">The haulier, or null.</param>
    /// <param name="storedTareKg">The stored tare, or null for none.</param>
    /// <param name="maxGrossKg">The permitted gross, or null for no check.</param>
    /// <param name="active">Whether the vehicle may use the bridge.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The vehicle's id, or why not.</returns>
    Task<Result<long>> SaveVehicleAsync(long companyId, string registration, string? haulier, decimal? storedTareKg, decimal? maxGrossKg, bool active = true, CancellationToken ct = default);
}
