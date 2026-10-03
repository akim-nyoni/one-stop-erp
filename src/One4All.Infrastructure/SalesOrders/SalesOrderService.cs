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
    private readonly One4All.Application.Abstractions.Numbering.INumberSequenceService _numbers;

    /// <summary>The document-type key of the sales-order number sequence.</summary>
    public const string OrderNumberSequence = "SO";

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service (for despatch stock issues).</param>
    /// <param name="salesLedger">The AR service (for invoicing).</param>
    /// <param name="pricing">The pricing service (resolves unpriced order lines from the price lists).</param>
    /// <param name="numbers">The number-sequence service (allocates order numbers left blank).</param>
    public SalesOrderService(One4AllDbContext db, IInventoryService inventory, ISalesLedgerService salesLedger, IPricingService pricing, One4All.Application.Abstractions.Numbering.INumberSequenceService numbers)
    {
        _db = db;
        _inventory = inventory;
        _salesLedger = salesLedger;
        _pricing = pricing;
        _numbers = numbers;
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

        // A blank order number means "allocate the next one" from the SO sequence.
        string orderNumber = request.OrderNumber.Trim();
        if (orderNumber.Length == 0)
        {
            Result<string> allocated = await _numbers.AllocateAsync(request.CompanyId, OrderNumberSequence, request.OrderDate, ct: ct).ConfigureAwait(false);
            if (allocated.IsFailure)
            {
                return allocated.Error;
            }

            orderNumber = allocated.Value;
        }

        SalesOrder order = new(request.CompanyId, request.CustomerId, orderNumber, request.OrderDate, request.Currency);
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
    public Task<Result<DespatchResult>> DespatchAsync(DespatchRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => DespatchCoreAsync(request, ct), ct);

    // Every line is checked before any stock moves; the stock issues and the order update then stand or fall together,
    // so a failure on a later line cannot leave earlier lines issued (a retry would issue them twice).
    private async Task<Result<DespatchResult>> DespatchCoreAsync(DespatchRequest request, CancellationToken ct)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {request.SalesOrderId} not found.");
        }

        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return Error.Rule("Sop.OrderCancelled", $"Sales order {order.OrderNumber} is cancelled.");
        }

        List<(SalesOrderLine Line, decimal Quantity)> despatches = [];
        foreach (IGrouping<long, DespatchLine> byLine in request.Lines.GroupBy(d => d.SalesOrderLineId))
        {
            SalesOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == byLine.Key);
            if (line is null)
            {
                return Error.NotFound("Sop.LineNotFound", $"Order line {byLine.Key} not found.");
            }

            decimal quantity = byLine.Sum(d => d.Quantity);
            if (quantity <= 0m)
            {
                return Error.Rule("Sop.InvalidQuantity", $"Order line {line.Id}: the despatch quantity must be positive.");
            }

            decimal remaining = line.OrderedQuantity - line.DespatchedQuantity;
            if (quantity > remaining + 0.0001m)
            {
                return Error.Rule("Sop.OverDespatch", $"Order line {line.Id}: cannot despatch {quantity}; only {remaining} left to despatch.");
            }

            despatches.Add((line, quantity));
        }

        decimal costOfSales = 0m;
        foreach ((SalesOrderLine line, decimal quantity) in despatches)
        {
            Item? item = await _db.Set<Item>().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
            if (item is { IsStockTracked: true } && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = request.WarehouseId,
                    Date = request.Date,
                    Quantity = quantity,
                    Reference = request.Reference,
                }, ct).ConfigureAwait(false);

                if (issue.IsFailure)
                {
                    return issue.Error;
                }

                costOfSales += issue.Value.Value;
            }

            line.Despatch(quantity);
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
    public Task<Result<InvoiceOrderResult>> InvoiceAsync(InvoiceOrderRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => InvoiceCoreAsync(request, ct), ct);

    // Every line is checked first; the lines are marked invoiced only once the AR invoice has posted, and both are
    // committed together — a refused invoice (customer on hold, over the credit limit) leaves the order untouched.
    private async Task<Result<InvoiceOrderResult>> InvoiceCoreAsync(InvoiceOrderRequest request, CancellationToken ct)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId && o.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {request.SalesOrderId} not found.");
        }

        decimal tax = 0m;
        List<(SalesOrderLine Line, decimal Quantity, decimal Net)> invoiced = [];
        foreach (IGrouping<long, InvoiceOrderLine> byLine in request.Lines.GroupBy(l => l.SalesOrderLineId))
        {
            SalesOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == byLine.Key);
            if (line is null)
            {
                return Error.NotFound("Sop.LineNotFound", $"Order line {byLine.Key} not found.");
            }

            decimal quantity = byLine.Sum(l => l.Quantity);
            decimal available = line.DespatchedQuantity - line.InvoicedQuantity;
            if (quantity <= 0m || quantity > available + 0.0001m)
            {
                return Error.Rule("Sop.NotDespatched", $"Order line {line.Id}: cannot invoice {quantity}; only {available} despatched and uninvoiced.");
            }

            invoiced.Add((line, quantity, decimal.Round(quantity * line.UnitPrice, 4, MidpointRounding.ToEven)));
            tax += byLine.Sum(l => l.TaxAmount);
        }

        if (invoiced.Count == 0)
        {
            return Error.Validation("Sop.NothingToInvoice", "An invoice needs at least one order line.");
        }

        // Revenue goes to each line's own revenue account.
        List<RevenueLine> revenue = [.. invoiced
            .GroupBy(i => i.Line.RevenueAccountCode, StringComparer.Ordinal)
            .Select(g => new RevenueLine(g.Key, g.Sum(i => i.Net)))];
        decimal net = revenue.Sum(r => r.NetAmount);

        Result<TradingPostingResult> invoice = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = request.CompanyId,
            CustomerId = order.CustomerId,
            DocumentNumber = request.InvoiceNumber,
            DocumentDate = request.Date,
            Currency = order.Currency,
            NetAmount = net,
            TaxAmount = tax,
            SalesAccountCode = revenue[0].AccountCode,
            RevenueLines = revenue,
            TaxAccountCode = request.TaxAccountCode,
            Narrative = $"Invoice for order {order.OrderNumber}",
        }, ct).ConfigureAwait(false);

        if (invoice.IsFailure)
        {
            return invoice.Error;
        }

        foreach ((SalesOrderLine line, decimal quantity, _) in invoiced)
        {
            line.Invoice(quantity);
        }

        order.RecomputeStatus();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new InvoiceOrderResult(invoice.Value.OpenItemId, invoice.Value.JournalNumber, net + tax);
    }
}
