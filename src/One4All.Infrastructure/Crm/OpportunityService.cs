using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Crm;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Crm;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Crm;

/// <summary>CRM opportunity operations, including conversion to a sales order.</summary>
public sealed class OpportunityService : IOpportunityService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesOrderService _salesOrders;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="salesOrders">The sales-order service.</param>
    public OpportunityService(One4AllDbContext db, ISalesOrderService salesOrders)
    {
        _db = db;
        _salesOrders = salesOrders;
    }

    /// <inheritdoc />
    public async Task<Result<PipelineForecast>> PipelineForecastAsync(long companyId, CancellationToken ct = default)
    {
        List<Opportunity> open = await _db.Set<Opportunity>()
            .Where(o => o.CompanyId == companyId && o.Stage != OpportunityStage.Won && o.Stage != OpportunityStage.Lost)
            .ToListAsync(ct).ConfigureAwait(false);

        List<PipelineStageSummary> stages = open
            .GroupBy(o => o.Stage)
            .OrderBy(g => g.Key)
            .Select(g => new PipelineStageSummary(
                g.Key,
                g.Count(),
                g.Sum(o => o.EstimatedValue),
                decimal.Round(g.Sum(o => o.WeightedValue), 2, MidpointRounding.ToEven)))
            .ToList();

        decimal totalEstimated = stages.Sum(s => s.EstimatedValue);
        decimal totalWeighted = decimal.Round(stages.Sum(s => s.WeightedValue), 2, MidpointRounding.ToEven);
        return new PipelineForecast(stages, totalEstimated, totalWeighted);
    }

    /// <inheritdoc />
    public async Task<Result<long>> ConvertToSalesOrderAsync(long companyId, long opportunityId, string orderNumber, DateOnly orderDate, CurrencyCode currency, IReadOnlyList<SalesOrderLineRequest> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0)
        {
            return Error.Validation("Crm.NoLines", "Converting an opportunity needs at least one order line.");
        }

        Opportunity? opportunity = await _db.Set<Opportunity>().FirstOrDefaultAsync(o => o.Id == opportunityId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (opportunity is null)
        {
            return Error.NotFound("Crm.OpportunityNotFound", $"Opportunity {opportunityId} not found.");
        }

        if (opportunity.Stage == OpportunityStage.Lost)
        {
            return Error.Rule("Crm.OpportunityLost", "A lost opportunity cannot be converted.");
        }

        if (opportunity.CustomerId is not { } customerId)
        {
            return Error.Rule("Crm.NoCustomer", "The opportunity has no linked customer to raise an order for.");
        }

        Result<SalesOrderResult> order = await _salesOrders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId,
            CustomerId = customerId,
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            Currency = currency,
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (order.IsFailure)
        {
            return order.Error;
        }

        opportunity.MoveTo(OpportunityStage.Won);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Value.SalesOrderId;
    }
}
