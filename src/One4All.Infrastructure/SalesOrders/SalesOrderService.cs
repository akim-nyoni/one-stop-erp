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
    private readonly One4All.Application.Abstractions.Security.IPermissionService? _permissions;
    private readonly Lazy<One4All.Application.Abstractions.Workflow.IWorkflowService>? _workflow;

    /// <summary>The document-type key of the sales-order number sequence.</summary>
    public const string OrderNumberSequence = "SO";

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service (for despatch stock issues).</param>
    /// <param name="salesLedger">The AR service (for invoicing).</param>
    /// <param name="pricing">The pricing service (resolves unpriced order lines from the price lists).</param>
    /// <param name="numbers">The number-sequence service (allocates order numbers left blank).</param>
    /// <param name="permissions">Checks who may authorise a discount beyond the limit.</param>
    /// <param name="workflow">The workflow service (a SalesDiscount workflow approves discounts beyond the limit).</param>
    public SalesOrderService(One4AllDbContext db, IInventoryService inventory, ISalesLedgerService salesLedger, IPricingService pricing, One4All.Application.Abstractions.Numbering.INumberSequenceService numbers,
        One4All.Application.Abstractions.Security.IPermissionService? permissions = null, Lazy<One4All.Application.Abstractions.Workflow.IWorkflowService>? workflow = null)
    {
        _permissions = permissions;
        _workflow = workflow;
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

        SalesOrder order = new(request.CompanyId, request.CustomerId, orderNumber, request.OrderDate, request.Currency) { RequestedDeliveryDate = request.RequestedDeliveryDate };
        decimal? limit = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.MaxSalesDiscountPercent).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        decimal deepest = 0m;
        List<(SalesOrderLineRequest Line, decimal? ListPrice)> listed = [];
        foreach (SalesOrderLineRequest line in request.Lines)
        {
            decimal unitPrice;
            if (line.UnitPrice is { } given)
            {
                unitPrice = given;

                // A keyed price below the price list is a discount: measured against the company's limit.
                if (limit is not null && await _pricing.PriceAsync(request.CompanyId, request.CustomerId, line.ItemId, line.Quantity, request.OrderDate, ct).ConfigureAwait(false) is
                    { IsSuccess: true } list && list.Value.Currency == request.Currency && list.Value.UnitPrice > 0m && given < list.Value.UnitPrice)
                {
                    deepest = Math.Max(deepest, (list.Value.UnitPrice - given) / list.Value.UnitPrice * 100m);
                    listed.Add((line, list.Value.UnitPrice));
                }
            }
            else
            {
                // No price keyed: take it from the customer's price list (then the default list) for this quantity.
                Result<ResolvedPrice> price = await _pricing.PriceAsync(request.CompanyId, request.CustomerId, line.ItemId, line.Quantity, request.OrderDate, ct).ConfigureAwait(false);
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

        foreach ((SalesOrderLineRequest line, decimal? listPrice) in listed)
        {
            order.Lines.First(l => l.ItemId == line.ItemId && l.Description == line.Description && l.OrderedQuantity == line.Quantity).ListUnitPrice = listPrice;
        }

        CreditLimitOverride? creditException = null;

        // Credit control at order taking (brief §4.5): the debtor balance, the orders not yet invoiced and this one, against the limit under the customer's policy.
        if (customer.CreditLimit > 0m && customer.CreditLimitPolicy != CreditLimitPolicy.Ignore
            && await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false) == request.Currency)
        {
            IQueryable<OpenItem> ar = _db.OpenItems.Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id);
            decimal balance = (await ar.Where(i => i.Sign == LedgerSign.Debit).SumAsync(i => (decimal?)i.OutstandingBase, ct).ConfigureAwait(false) ?? 0m)
                - (await ar.Where(i => i.Sign == LedgerSign.Credit).SumAsync(i => (decimal?)i.OutstandingBase, ct).ConfigureAwait(false) ?? 0m);
            decimal onOrder = await _db.Set<SalesOrder>().Where(o => o.CompanyId == request.CompanyId && o.CustomerId == customer.Id && o.Currency == request.Currency
                    && o.Status != SalesOrderStatus.Cancelled)
                .SelectMany(o => o.Lines).SumAsync(l => (decimal?)((l.OrderedQuantity - l.InvoicedQuantity) * l.UnitPrice), ct).ConfigureAwait(false) ?? 0m;
            decimal exposure = balance + onOrder + order.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice);
            if (exposure > customer.CreditLimit)
            {
                long overrider = 0;
                if (customer.CreditLimitPolicy == CreditLimitPolicy.Block)
                {
                    if (request.CreditLimitOverrideByUserId is not long given)
                    {
                        return Error.Rule("Sop.CreditLimitExceeded", $"Customer {customer.Code}: with this order the exposure would be {exposure:0.00} (balance {balance:0.00}, "
                            + $"on order {onOrder:0.00}), over the credit limit of {customer.CreditLimit:0.00}.");
                    }

                    if (given == request.RaisedByUserId)
                    {
                        return Error.Rule("Sop.CreditOverrideSelf", "Whoever raises the order cannot override the credit limit for it.");
                    }

                    if (_permissions is null || !(await _permissions.ResolveAsync(given, ct).ConfigureAwait(false)).Has("Sales.Customer", Core.Security.OperationFlags.OverrideCreditLimit))
                    {
                        return Error.Forbidden("Sop.CreditOverrideNotAllowed", "Only a user who may override credit limits can let this order through.");
                    }

                    overrider = given;
                }

                // A warning, or an authorised override, is kept for the credit-limit exceptions report.
                creditException = new CreditLimitOverride(request.CompanyId, customer.Id, orderNumber, customer.CreditLimit, exposure, overrider, request.RaisedByUserId, DateTimeOffset.UtcNow);
            }
        }

        bool overLimit = limit is decimal max && deepest > max + 0.0001m;
        if (overLimit && request.DiscountAuthorisedByUserId is long authoriser)
        {
            if (authoriser == request.RaisedByUserId)
            {
                return Error.Forbidden("Sop.DiscountSelfAuthorised", "Whoever raises the order cannot authorise its discount.");
            }

            if (_permissions is not null && !(await _permissions.ResolveAsync(authoriser, ct).ConfigureAwait(false)).Has("Sales.Invoice", Core.Security.OperationFlags.OverrideDiscount))
            {
                return Error.Forbidden("Sop.DiscountAuthority", $"A {deepest:0.##}% discount is beyond the {limit:0.##}% limit and the authoriser may not override discounts.");
            }

            order.ApproveDiscount(authoriser);
            overLimit = false;
        }

        // No authoriser: only the company's SalesDiscount workflow can let it through; without one the order is refused, unsaved.
        if (overLimit && (request.RaisedByUserId is null || _workflow is null
            || !await _db.WorkflowDefinitions.AnyAsync(d => d.CompanyId == request.CompanyId && d.DocumentType == Application.Abstractions.Workflow.WorkflowDocumentTypes.SalesDiscount && d.IsActive, ct)
                .ConfigureAwait(false)))
        {
            return Error.Rule("Sop.DiscountOverLimit", $"A {deepest:0.##}% discount is beyond the {limit:0.##}% limit: an authoriser who may override discounts must sign it off.");
        }

        _db.Set<SalesOrder>().Add(order);
        if (creditException is not null)
        {
            _db.Set<CreditLimitOverride>().Add(creditException);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (overLimit)
        {
            // No authoriser: the company's SalesDiscount workflow approves it, or the order is refused.
            Result<bool> governed = request.RaisedByUserId is long raiser
                ? await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, request.CompanyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.SalesDiscount, order.OrderNumber,
                    order.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice), raiser, [("Currency", order.Currency.Value), ("Discount", deepest.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))], ct)
                    .ConfigureAwait(false)
                : false;
            if (governed.IsFailure || !governed.Value)
            {
                return governed.IsFailure ? governed.Error
                    : Error.Rule("Sop.DiscountOverLimit", $"A {deepest:0.##}% discount is beyond the {limit:0.##}% limit: an authoriser who may override discounts must sign it off.");
            }

            order.HoldForDiscount();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new SalesOrderResult(order.Id, order.OrderNumber);
    }

    /// <inheritdoc />
    public Task<Result<StockReservationResult>> ReserveAsync(long companyId, long salesOrderId, long warehouseId, CancellationToken ct = default)
        => _db.AtomicAsync<StockReservationResult>(async () =>
        {
            SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
            if (order is null)
            {
                return Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found.");
            }

            if (order.Status == SalesOrderStatus.Cancelled)
            {
                return Error.Rule("Sop.OrderCancelled", $"Sales order {order.OrderNumber} is cancelled.");
            }

            if (!await _db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.CompanyId == companyId, ct).ConfigureAwait(false))
            {
                return Error.NotFound("Inv.WarehouseNotFound", $"Warehouse {warehouseId} not found.");
            }

            decimal reserved = 0m, shortBy = 0m;
            foreach (SalesOrderLine line in order.Lines.Where(l => l.UnreservedQuantity > 0m))
            {
                if (line.ReservedWarehouseId is long held && held != warehouseId)
                {
                    shortBy += line.UnreservedQuantity;
                    continue;
                }

                decimal free = await StockReservations.FreeAsync(_db, companyId, line.ItemId, warehouseId, null, ct).ConfigureAwait(false);
                decimal take = Math.Min(line.UnreservedQuantity, Math.Max(0m, free));
                shortBy += line.UnreservedQuantity - take;
                if (take > 0m)
                {
                    line.Reserve(warehouseId, take);
                    reserved += take;
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<StockReservationResult>.Success(new StockReservationResult(reserved, shortBy));
        }, ct);

    /// <inheritdoc />
    public async Task<Result> ReleaseReservationsAsync(long companyId, long salesOrderId, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found."));
        }

        foreach (SalesOrderLine line in order.Lines)
        {
            line.ReleaseReservation();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
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

        if (order.AwaitingDiscountApproval)
        {
            return Error.Rule("Sop.DiscountApproval", $"Sales order {order.OrderNumber} waits for its discount to be approved.");
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
            // A kit despatches its components.
            foreach ((Item item, decimal issued) in await Inventory.KitExplosion.StockLinesAsync(_db, request.CompanyId, line.ItemId, quantity, ct).ConfigureAwait(false))
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId,
                    ItemId = item.Id,
                    WarehouseId = request.WarehouseId,
                    Date = request.Date,
                    Quantity = issued,
                    Reference = request.Reference,
                    SalesOrderLineId = line.Id,
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
        SalesDelivery delivery = new(request.CompanyId, order.Id, request.Reference, request.Date)
        {
            PlannedFor = request.PlannedFor ?? request.Date, Vehicle = request.Vehicle?.Trim().ToUpperInvariant(), Route = request.Route?.Trim(), Driver = request.Driver?.Trim(),
        };
        despatches.ForEach(d => delivery.AddLine(d.Line.Id, d.Line.ItemId, d.Quantity));
        _db.SalesDeliveries.Add(delivery);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DespatchResult(costOfSales);
    }

    /// <inheritdoc />
    public async Task<Result> ConfirmDeliveryAsync(long companyId, string deliveryReference, DateOnly deliveredOn, string receivedBy, string? podReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(receivedBy))
        {
            return Result.Failure(Error.Validation("Sop.PodSignature", "Proof of delivery needs the name of whoever received the goods."));
        }

        SalesDelivery? delivery = await _db.SalesDeliveries.FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Reference == deliveryReference, ct).ConfigureAwait(false);
        if (delivery is null)
        {
            return Result.Failure(Error.NotFound("Sop.DeliveryNotFound", $"No delivery {deliveryReference}."));
        }

        if (delivery.DeliveredOn is not null)
        {
            return Result.Failure(Error.Rule("Sop.DeliveryConfirmed", $"Delivery {deliveryReference} was already signed for on {delivery.DeliveredOn:yyyy-MM-dd}."));
        }

        if (deliveredOn < delivery.DespatchedOn)
        {
            return Result.Failure(Error.Validation("Sop.DeliveredBeforeDespatch", "Goods cannot arrive before they were despatched."));
        }

        delivery.ConfirmDelivery(deliveredOn, receivedBy.Trim(), podReference?.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
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

        decimal returnedValue = 0m;
        foreach ((Item item, decimal returned) in await Inventory.KitExplosion.StockLinesAsync(_db, companyId, line.ItemId, quantity, ct).ConfigureAwait(false))
        {
            // Return the goods (a kit's components) to stock at the current average cost, reversing cost of sales.
            decimal averageCost = await _db.Set<StockBalance>()
                .Where(b => b.CompanyId == companyId && b.ItemId == item.Id && b.WarehouseId == warehouseId)
                .Select(b => b.AverageCost).FirstOrDefaultAsync(ct).ConfigureAwait(false);

            Result<StockMovementResult> receipt = await _inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId,
                ItemId = item.Id,
                WarehouseId = warehouseId,
                Date = date,
                Quantity = returned,
                UnitCost = averageCost,
                CreditAccountCode = item.CostOfSalesAccountCode!,
                Reference = $"RET:{order.OrderNumber}",
            }, ct).ConfigureAwait(false);

            if (receipt.IsFailure)
            {
                return receipt.Error;
            }

            returnedValue += receipt.Value.Value;
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
    // committed together - a refused invoice (customer on hold, over the credit limit) leaves the order untouched.
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

/// <summary>Concludes the SalesDiscount workflow: an approved discount releases the order; a refused one cancels it.</summary>
internal sealed class SalesDiscountWorkflowHandler : Application.Abstractions.Workflow.IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;

    public SalesDiscountWorkflowHandler(One4AllDbContext db) => _db = db;

    public string DocumentType => Application.Abstractions.Workflow.WorkflowDocumentTypes.SalesDiscount;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber == documentReference && o.AwaitingDiscountApproval, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Success();
        }

        order.ApproveDiscount(decidedByUserId);
        if (outcome != Core.Workflow.WorkflowStatus.Approved)
        {
            Result cancelled = order.Cancel();
            if (cancelled.IsFailure)
            {
                return cancelled;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
