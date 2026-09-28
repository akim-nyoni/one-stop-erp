using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Crm;

namespace One4All.Application.Abstractions.Crm;

/// <summary>The pipeline summary for one open opportunity stage.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Count">The number of open opportunities at the stage.</param>
/// <param name="EstimatedValue">The total estimated (unweighted) value.</param>
/// <param name="WeightedValue">The total probability-weighted value (the forecast contribution).</param>
public readonly record struct PipelineStageSummary(OpportunityStage Stage, int Count, decimal EstimatedValue, decimal WeightedValue);

/// <summary>The weighted-pipeline forecast across the open opportunity stages.</summary>
/// <param name="Stages">The per-stage summaries, in stage order.</param>
/// <param name="TotalEstimated">The total estimated value of the open pipeline.</param>
/// <param name="TotalWeighted">The total probability-weighted forecast.</param>
public sealed record PipelineForecast(IReadOnlyList<PipelineStageSummary> Stages, decimal TotalEstimated, decimal TotalWeighted);

/// <summary>Lightweight CRM opportunity operations that reach into other modules.</summary>
public interface IOpportunityService
{
    /// <summary>
    /// Produces the weighted-pipeline forecast for the open opportunities: their estimated and
    /// probability-weighted value grouped by stage, plus the overall totals. Won and lost opportunities are
    /// excluded (they are no longer in the pipeline).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pipeline forecast.</returns>
    Task<Result<PipelineForecast>> PipelineForecastAsync(long companyId, CancellationToken ct = default);

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
