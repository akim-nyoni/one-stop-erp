using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>One line of a quotation to create.</summary>
/// <param name="ItemId">The stock item.</param>
/// <param name="Description">The line description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="TaxCode">The tax code, if any.</param>
/// <param name="RevenueAccountCode">The revenue account for the eventual order.</param>
public readonly record struct QuotationLineRequest(long ItemId, string Description, decimal Quantity, decimal UnitPrice, string? TaxCode, string RevenueAccountCode);

/// <summary>
/// Manages sales quotations (brief §4.13): capture, sending, and the customer's decision. Quotations touch no
/// ledger; an accepted quote converts into a sales order (through <see cref="ISalesOrderService"/>), which then
/// drives despatch and invoicing.
/// </summary>
public interface IQuotationService
{
    /// <summary>Creates a draft quotation with its lines.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="quoteNumber">The quotation number.</param>
    /// <param name="quoteDate">The quotation date.</param>
    /// <param name="validUntil">The date the quote lapses (must not be before the quote date).</param>
    /// <param name="currency">The quotation currency.</param>
    /// <param name="lines">The quotation lines (at least one, each with a positive quantity).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created quotation id.</returns>
    Task<Result<long>> CreateAsync(long companyId, long customerId, string quoteNumber, DateOnly quoteDate, DateOnly validUntil, CurrencyCode currency, IReadOnlyList<QuotationLineRequest> lines, CancellationToken ct = default);

    /// <summary>Marks a draft quotation as sent to the customer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="quotationId">The quotation.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> SendAsync(long companyId, long quotationId, CancellationToken ct = default);

    /// <summary>Records the customer's acceptance (from draft or sent).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="quotationId">The quotation.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> AcceptAsync(long companyId, long quotationId, CancellationToken ct = default);

    /// <summary>Records the customer's decline (from draft or sent).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="quotationId">The quotation.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> DeclineAsync(long companyId, long quotationId, CancellationToken ct = default);

    /// <summary>
    /// Converts an accepted quotation into a sales order: creates the order (with the quote's lines) through the
    /// sales-order service and marks the quotation converted. Refuses a quotation that is not accepted.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="quotationId">The quotation.</param>
    /// <param name="orderNumber">The order number for the new order.</param>
    /// <param name="orderDate">The order date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created sales order id.</returns>
    Task<Result<long>> ConvertToOrderAsync(long companyId, long quotationId, string orderNumber, DateOnly orderDate, CancellationToken ct = default);
}
