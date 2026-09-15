using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Sales Order Processing (brief §4.13): order → despatch (stock issue) → invoice (AR).</summary>
public sealed class SalesOrderService : ISalesOrderService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly ISalesLedgerService _salesLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service (for despatch stock issues).</param>
    /// <param name="salesLedger">The AR service (for invoicing).</param>
    public SalesOrderService(One4AllDbContext db, IInventoryService inventory, ISalesLedgerService salesLedger)
    {
        _db = db;
        _inventory = inventory;
        _salesLedger = salesLedger;
    }

    /// <inheritdoc />
    public async Task<Result<SalesOrderResult>> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default)
    {
        SalesOrder order = new(request.CompanyId, request.CustomerId, request.OrderNumber, request.OrderDate, request.Currency);
        foreach (SalesOrderLineRequest line in request.Lines)
        {
            order.AddLine(line.ItemId, line.Description, line.Quantity, line.UnitPrice, line.TaxCode, line.RevenueAccountCode);
        }

        _db.Set<SalesOrder>().Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SalesOrderResult(order.Id, order.OrderNumber);
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
