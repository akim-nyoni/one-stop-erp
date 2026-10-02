using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Sales Order Processing (brief §4.13): order → despatch (stock issue) → invoice (AR).</summary>
public sealed class SalesOrderService : ISalesOrderService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IPricingService _pricing;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service (for despatch stock issues).</param>
    /// <param name="salesLedger">The AR service (for invoicing).</param>
    /// <param name="pricing">The pricing service (resolves unpriced order lines from the price lists).</param>
    public SalesOrderService(One4AllDbContext db, IInventoryService inventory, ISalesLedgerService salesLedger, IPricingService pricing)
    {
        _db = db;
        _inventory = inventory;
        _salesLedger = salesLedger;
        _pricing = pricing;
    }

    /// <inheritdoc />
    public async Task<Result<SalesOrderResult>> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        // Credit control: a customer on stop may not place new orders (invoicing applies the same rule).
        Customer? customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct)
            .ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Sop.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        if (customer.OnHold)
        {
            return Error.Rule("Sop.CustomerOnHold", $"Customer {customer.Code} is on hold; no new sales orders can be taken.");
        }

        SalesOrder order = new(request.CompanyId, request.CustomerId, request.OrderNumber, request.OrderDate, request.Currency);
        foreach (SalesOrderLineRequest line in request.Lines)
        {
            decimal unitPrice;
            if (line.UnitPrice is { } given)
            {
                unitPrice = given;
            }
            else
            {
                // No price keyed: take it from the customer's price list (then the default list) for this quantity.
                Result<ResolvedPrice> price = await _pricing.PriceAsync(request.CompanyId, request.CustomerId, line.ItemId, line.Quantity, ct).ConfigureAwait(false);
                if (price.IsFailure)
                {
                    return price.Error;
                }

                if (price.Value.Currency != request.Currency)
                {
                    return Error.Rule("Sop.PriceCurrency", $"Price list {price.Value.PriceListCode} is in {price.Value.Currency} but the order is in {request.Currency}.");
                }

                unitPrice = price.Value.UnitPrice;
            }

            order.AddLine(line.ItemId, line.Description, line.Quantity, unitPrice, line.TaxCode, line.RevenueAccountCode);
        }

        _db.Set<SalesOrder>().Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SalesOrderResult(order.Id, order.OrderNumber);
    }

    /// <inheritdoc />
    public async Task<Result> CancelOrderAsync(long companyId, long salesOrderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Sop.ReasonRequired", "A reason is required to cancel a sales order."));
        }

        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found."));
        }

        Result cancelled = order.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<DespatchResult>> DespatchAsync(DespatchRequest request, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {request.SalesOrderId} not found.");
        }

        decimal costOfSales = 0m;
        foreach (DespatchLine despatch in request.Lines)
        {
            SalesOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == despatch.SalesOrderLineId);
            if (line is null)
            {
                return Error.NotFound("Sop.LineNotFound", $"Order line {despatch.SalesOrderLineId} not found.");
            }

            Item? item = await _db.Set<Item>().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
            if (item is { IsStockTracked: true } && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = request.WarehouseId,
                    Date = request.Date,
                    Quantity = despatch.Quantity,
                    Reference = request.Reference,
                }, ct).ConfigureAwait(false);

                if (issue.IsFailure)
                {
                    return issue.Error;
                }

                costOfSales += issue.Value.Value;
            }

            line.Despatch(despatch.Quantity);
        }

        order.RecomputeStatus();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DespatchResult(costOfSales);
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> ReturnDespatchAsync(long companyId, long salesOrderId, long salesOrderLineId, long warehouseId, DateOnly date, decimal quantity, string reason, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Error.Rule("Sop.InvalidQuantity", "The return quantity must be positive.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Sop.ReasonRequired", "A despatch return requires a reason.");
        }

        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found.");
        }

        SalesOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == salesOrderLineId);
        if (line is null)
        {
            return Error.NotFound("Sop.LineNotFound", $"Order line {salesOrderLineId} not found.");
        }

        decimal returnable = line.DespatchedQuantity - line.InvoicedQuantity;
        if (quantity > returnable)
        {
            return Error.Rule("Sop.NotReturnable", $"Order line {line.Id}: cannot return {quantity}; only {returnable} despatched and uninvoiced.");
        }

        Item? item = await _db.Set<Item>().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
        decimal returnedValue = 0m;
        if (item is { IsStockTracked: true } && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
        {
            // Return the goods to stock at the current average cost, reversing cost of sales.
            decimal averageCost = await _db.Set<StockBalance>()
                .Where(b => b.CompanyId == companyId && b.ItemId == line.ItemId && b.WarehouseId == warehouseId)
                .Select(b => b.AverageCost).FirstOrDefaultAsync(ct).ConfigureAwait(false);

            Result<StockMovementResult> receipt = await _inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId,
                ItemId = line.ItemId,
                WarehouseId = warehouseId,
                Date = date,
                Quantity = quantity,
                UnitCost = averageCost,
                CreditAccountCode = item.CostOfSalesAccountCode!,
                Reference = $"RET:{order.OrderNumber}",
            }, ct).ConfigureAwait(false);

            if (receipt.IsFailure)
            {
                return receipt.Error;
            }

            returnedValue = receipt.Value.Value;
        }

        line.ReturnDespatch(quantity);
        order.RecomputeStatus();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return returnedValue;
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceOrderResult>> InvoiceAsync(InvoiceOrderRequest request, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {request.SalesOrderId} not found.");
        }

        decimal net = 0m;
        decimal tax = 0m;
        string revenueAccount = string.Empty;

        foreach (InvoiceOrderLine invLine in request.Lines)
        {
            SalesOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == invLine.SalesOrderLineId);
            if (line is null)
            {
                return Error.NotFound("Sop.LineNotFound", $"Order line {invLine.SalesOrderLineId} not found.");
            }

            decimal available = line.DespatchedQuantity - line.InvoicedQuantity;
            if (invLine.Quantity > available + 0.0001m)
            {
                return Error.Rule("Sop.NotDespatched", $"Order line {line.Id}: cannot invoice {invLine.Quantity}; only {available} despatched and uninvoiced.");
            }

            net += decimal.Round(invLine.Quantity * line.UnitPrice, 4, MidpointRounding.ToEven);
            tax += invLine.TaxAmount;
            revenueAccount = line.RevenueAccountCode;
            line.Invoice(invLine.Quantity);
        }

        Result<TradingPostingResult> invoice = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = request.CompanyId,
            CustomerId = order.CustomerId,
            DocumentNumber = request.InvoiceNumber,
            DocumentDate = request.Date,
            Currency = order.Currency,
            NetAmount = net,
            TaxAmount = tax,
            SalesAccountCode = revenueAccount,
            TaxAccountCode = request.TaxAccountCode,
            Narrative = $"Invoice for order {order.OrderNumber}",
        }, ct).ConfigureAwait(false);

        if (invoice.IsFailure)
        {
            return invoice.Error;
        }

        order.RecomputeStatus();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new InvoiceOrderResult(invoice.Value.OpenItemId, invoice.Value.JournalNumber, net + tax);
    }
}
