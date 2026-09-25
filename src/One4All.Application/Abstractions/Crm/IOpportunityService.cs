using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Crm;

/// <summary>Lightweight CRM opportunity operations that reach into other modules.</summary>
public interface IOpportunityService
{
    /// <summary>
    /// Converts a won (or to-be-won) opportunity into a sales order for its linked customer: creates the order
    /// through the sales-order service with the supplied lines and marks the opportunity Won. Refuses a lost
    /// opportunity, one with no linked customer, or an empty line list.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="opportunityId">The opportunity.</param>
    /// <param name="orderNumber">The sales-order number.</param>
    /// <param name="orderDate">The order date.</param>
    /// <param name="currency">The order currency.</param>
    /// <param name="lines">The order lines (the quoted items).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created sales order id.</returns>
    Task<Result<long>> ConvertToSalesOrderAsync(long companyId, long opportunityId, string orderNumber, DateOnly orderDate, CurrencyCode currency, IReadOnlyList<SalesOrderLineRequest> lines, CancellationToken ct = default);
}
