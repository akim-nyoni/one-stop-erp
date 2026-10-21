using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Security;
using One4All.Application.Api;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Core.SalesOrders;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using static One4All.Server.TradingApiEndpoints;

namespace One4All.Server;

/// <summary>
/// The REST API's order and stock endpoints (brief §11): sales orders (create, despatch, invoice, list, read), purchase orders (create,
/// receive goods, match the supplier's invoice, list, read) and stock (on hand, valuation, receipts, issues, transfers, adjustments). Documents
/// name customers, suppliers, items and warehouses by code; each step runs through the same application service the desktop uses. A
/// warehouse named by code is held to the user's warehouse scope, as a warehouseId would be by the access middleware.
/// </summary>
internal static class OrdersApiEndpoints
{
    /// <summary>Maps the endpoints onto the API group.</summary>
    /// <param name="api">The API route group (authorisation is required on every route).</param>
    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        MapSalesOrders(api);
        MapPurchaseOrders(api);
        MapStock(api);
    }

    private static void MapSalesOrders(RouteGroupBuilder api)
    {
        api.MapGet("/api/sop/orders", async (long companyId, string? customer, string? status, DateOnly? from, DateOnly? to, One4AllDbContext db, CancellationToken ct) =>
        {
            if (!TryStatus(status, out SalesOrderStatus? wanted))
            {
                return BadRequest($"'{status}' is not a sales order status ({string.Join(", ", Enum.GetNames<SalesOrderStatus>())}).");
            }

            string? customerKey = string.IsNullOrWhiteSpace(customer) ? null : OrdersApiMapping.Key(customer);
            var rows = await (from o in db.SalesOrders.AsNoTracking()
                              join c in db.Customers.AsNoTracking() on o.CustomerId equals c.Id
                              where o.CompanyId == companyId && (customerKey == null || c.Code.ToUpper() == customerKey) && (wanted == null || o.Status == wanted)
                                  && (@from == null || o.OrderDate >= @from) && (to == null || o.OrderDate <= to)
                              orderby o.OrderDate descending, o.OrderNumber descending
                              select new { o.OrderNumber, c.Code, o.OrderDate, o.Status, o.Currency, Total = o.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice) }).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows.Select(r => new SalesOrderRowApi(r.OrderNumber, r.Code, r.OrderDate, r.Status.ToString(), r.Currency.Value, r.Total)).ToList());
        }).RequireAuthorization().WithName("ListSalesOrders");

        api.MapGet("/api/sop/orders/{number}", async (string number, long companyId, One4AllDbContext db, CancellationToken ct) =>
        {
            SalesOrder? order = await SalesOrderAsync(db, companyId, number, ct).ConfigureAwait(false);
            if (order is null)
            {
                return NotFound($"No sales order {OrdersApiMapping.Key(number)}.");
            }

            string customer = await db.Customers.AsNoTracking().Where(c => c.Id == order.CustomerId).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
            Dictionary<long, string> codes = await ItemCodesAsync(db, order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
            return Results.Ok(new SalesOrderViewApi(order.OrderNumber, customer, order.OrderDate, order.Status.ToString(), order.Currency.Value, order.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice),
                order.RequestedDeliveryDate,
                [.. order.Lines.Select(l => new SalesOrderLineViewApi(codes.GetValueOrDefault(l.ItemId, string.Empty), l.Description, l.OrderedQuantity, l.DespatchedQuantity, l.InvoicedQuantity, l.UnitPrice,
                    l.TaxCode, l.RevenueAccountCode, l.UnitCode))]));
        }).RequireAuthorization().WithName("GetSalesOrder");

        api.MapPost("/api/sop/orders", async (long companyId, long userId, SalesOrderApi request, One4AllDbContext db, ISalesOrderService orders, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            string key = OrdersApiMapping.Key(request.CustomerCode);
            var customer = await db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code.ToUpper() == key).Select(c => new { c.Id, c.Currency }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (customer is null)
            {
                return NotFound($"No customer {key}.");
            }

            Dictionary<string, ApiItem> items = await ItemsAsync(db, companyId, (request.Lines ?? []).Select(l => l.ItemCode), ct).ConfigureAwait(false);
            Result<CreateSalesOrderRequest> mapped = OrdersApiMapping.SalesOrder(companyId, userId, request, new ApiParty(customer.Id, customer.Currency), items);
            return mapped.IsFailure ? Value(mapped) : Value(await orders.CreateOrderAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("CreateSalesOrder");

        api.MapPost("/api/sop/orders/{number}/despatch", async (string number, long companyId, long userId, DespatchApi request, One4AllDbContext db, IDataScopeService scopes, ISalesOrderService orders,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            SalesOrder? order = await SalesOrderAsync(db, companyId, number, ct).ConfigureAwait(false);
            if (order is null)
            {
                return NotFound($"No sales order {OrdersApiMapping.Key(number)}.");
            }

            Result<long> warehouse = await WarehouseAsync(db, scopes, companyId, userId, request.WarehouseCode, ct).ConfigureAwait(false);
            if (warehouse.IsFailure)
            {
                return Value(warehouse);
            }

            Dictionary<long, string> codes = await ItemCodesAsync(db, order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
            List<ApiOrderLine> lines = [.. order.Lines.Select(l => new ApiOrderLine(l.Id, codes.GetValueOrDefault(l.ItemId, string.Empty), l.OrderedQuantity - l.DespatchedQuantity, l.UnitPrice))];
            Result<DespatchRequest> mapped = OrdersApiMapping.Despatch(companyId, order.Id, order.OrderNumber, request, warehouse.Value, lines);
            return mapped.IsFailure ? Value(mapped) : Value(await orders.DespatchAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("DespatchSalesOrder");

        api.MapPost("/api/sop/orders/{number}/invoice", async (string number, long companyId, SalesOrderInvoiceApi request, One4AllDbContext db, ISalesOrderService orders, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            SalesOrder? order = await SalesOrderAsync(db, companyId, number, ct).ConfigureAwait(false);
            if (order is null)
            {
                return NotFound($"No sales order {OrdersApiMapping.Key(number)}.");
            }

            Dictionary<long, string> codes = await ItemCodesAsync(db, order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
            List<string> taxCodes = [.. order.Lines.Select(l => l.TaxCode).OfType<string>().Distinct()];
            List<Core.Accounting.TaxCode> taxes = await db.Set<Core.Accounting.TaxCode>().AsNoTracking().Include(t => t.Rates).Where(t => t.CompanyId == companyId && taxCodes.Contains(t.Code))
                .ToListAsync(ct).ConfigureAwait(false);
            decimal Rate(string? code) => code is not null && taxes.Find(t => t.Code == code) is { } tax ? tax.RateOn(request.Date) : 0m;
            List<ApiOrderLine> lines = [.. order.Lines.Select(l => new ApiOrderLine(l.Id, codes.GetValueOrDefault(l.ItemId, string.Empty), l.DespatchedQuantity - l.InvoicedQuantity, l.UnitPrice, Rate(l.TaxCode)))];
            Result<InvoiceOrderRequest> mapped = OrdersApiMapping.SalesOrderInvoice(companyId, order.Id, order.OrderNumber, request, lines);
            return mapped.IsFailure ? Value(mapped) : Value(await orders.InvoiceAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("InvoiceSalesOrder");
    }

    private static void MapPurchaseOrders(RouteGroupBuilder api)
    {
        api.MapGet("/api/pop/orders", async (long companyId, string? supplier, string? status, DateOnly? from, DateOnly? to, One4AllDbContext db, CancellationToken ct) =>
        {
            if (!TryStatus(status, out PurchaseOrderStatus? wanted))
            {
                return BadRequest($"'{status}' is not a purchase order status ({string.Join(", ", Enum.GetNames<PurchaseOrderStatus>())}).");
            }

            string? supplierKey = string.IsNullOrWhiteSpace(supplier) ? null : OrdersApiMapping.Key(supplier);
            var rows = await (from o in db.PurchaseOrders.AsNoTracking()
                              join s in db.Suppliers.AsNoTracking() on o.SupplierId equals s.Id
                              where o.CompanyId == companyId && (supplierKey == null || s.Code.ToUpper() == supplierKey) && (wanted == null || o.Status == wanted)
                                  && (@from == null || o.OrderDate >= @from) && (to == null || o.OrderDate <= to)
                              orderby o.OrderDate descending, o.OrderNumber descending
                              select new { o.OrderNumber, s.Code, o.OrderDate, o.Status, o.Currency, Total = o.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice) }).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows.Select(r => new PurchaseOrderRowApi(r.OrderNumber, r.Code, r.OrderDate, r.Status.ToString(), r.Currency.Value, r.Total)).ToList());
        }).RequireAuthorization().WithName("ListPurchaseOrders");

        api.MapGet("/api/pop/orders/{number}", async (string number, long companyId, One4AllDbContext db, CancellationToken ct) =>
        {
            PurchaseOrder? order = await PurchaseOrderAsync(db, companyId, number, ct).ConfigureAwait(false);
            if (order is null)
            {
                return NotFound($"No purchase order {OrdersApiMapping.Key(number)}.");
            }

            string supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == order.SupplierId).Select(s => s.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
            Dictionary<long, string> codes = await ItemCodesAsync(db, order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
            return Results.Ok(new PurchaseOrderViewApi(order.OrderNumber, supplier, order.OrderDate, order.Status.ToString(), order.Currency.Value, order.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice),
                order.RequiredBy, order.AwaitingApproval,
                [.. order.Lines.Select(l => new PurchaseOrderLineViewApi(codes.GetValueOrDefault(l.ItemId, string.Empty), l.Description, l.OrderedQuantity, l.ReceivedQuantity, l.InvoicedQuantity, l.UnitPrice, l.UnitCode))]));
        }).RequireAuthorization().WithName("GetPurchaseOrder");

        api.MapPost("/api/pop/orders", async (long companyId, long userId, PurchaseOrderApi request, One4AllDbContext db, IProcurementService procurement, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            string key = OrdersApiMapping.Key(request.SupplierCode);
            var supplier = await db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId && s.Code.ToUpper() == key).Select(s => new { s.Id, s.Currency }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (supplier is null)
            {
                return NotFound($"No supplier {key}.");
            }

            Dictionary<string, ApiItem> items = await ItemsAsync(db, companyId, (request.Lines ?? []).Select(l => l.ItemCode), ct).ConfigureAwait(false);
            Result<CreatePurchaseOrderRequest> mapped = OrdersApiMapping.PurchaseOrder(companyId, userId, request, new ApiParty(supplier.Id, supplier.Currency), items);
            return mapped.IsFailure ? Value(mapped) : Value(await procurement.CreatePurchaseOrderAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("CreatePurchaseOrder");

        api.MapPost("/api/pop/orders/{number}/receipts", async (string number, long companyId, long userId, GoodsReceiptApi request, One4AllDbContext db, IDataScopeService scopes,
            INumberSequenceService numbers, IProcurementService procurement, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            PurchaseOrder? order = await PurchaseOrderAsync(db, companyId, number, ct).ConfigureAwait(false);
            if (order is null)
            {
                return NotFound($"No purchase order {OrdersApiMapping.Key(number)}.");
            }

            Result<long> warehouse = await WarehouseAsync(db, scopes, companyId, userId, request.WarehouseCode, ct).ConfigureAwait(false);
            if (warehouse.IsFailure)
            {
                return Value(warehouse);
            }

            Dictionary<long, string> codes = await ItemCodesAsync(db, order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
            List<ApiOrderLine> lines = [.. order.Lines.Select(l => new ApiOrderLine(l.Id, codes.GetValueOrDefault(l.ItemId, string.Empty), l.OrderedQuantity - l.ReceivedQuantity, l.UnitPrice))];
            Result<ReceiveGoodsRequest> mapped = OrdersApiMapping.GoodsReceipt(companyId, userId, order.Id, order.OrderNumber, request, warehouse.Value, lines);
            if (mapped.IsSuccess && mapped.Value.GrnNumber.Length == 0)
            {
                // No GRN number given: the next in the company's GRN sequence, as the desktop allocates it (only once the receipt is otherwise valid).
                Result<string> grn = await numbers.AllocateAsync(companyId, "GRN", request.Date, ct: ct).ConfigureAwait(false);
                if (grn.IsFailure)
                {
                    return Value(grn);
                }

                mapped = OrdersApiMapping.GoodsReceipt(companyId, userId, order.Id, order.OrderNumber, request with { GrnNumber = grn.Value }, warehouse.Value, lines);
            }

            if (mapped.IsFailure)
            {
                return Value(mapped);
            }

            Result<GrnResult> received = await procurement.ReceiveGoodsAsync(mapped.Value, ct).ConfigureAwait(false);
            return received.IsFailure ? Value(received) : Results.Ok(new { received.Value.GrnId, mapped.Value.GrnNumber, received.Value.AccruedValue });
        }).RequireAuthorization().WithName("ReceivePurchaseOrder");

        api.MapPost("/api/pop/invoices/match", async (long companyId, InvoiceMatchApi request, One4AllDbContext db, IProcurementService procurement, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            PurchaseOrder? order = await PurchaseOrderAsync(db, companyId, request.OrderNumber, ct).ConfigureAwait(false);
            if (order is null)
            {
                return NotFound($"No purchase order {OrdersApiMapping.Key(request.OrderNumber)}.");
            }

            Dictionary<long, string> codes = await ItemCodesAsync(db, order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
            List<ApiOrderLine> lines = [.. order.Lines.Select(l => new ApiOrderLine(l.Id, codes.GetValueOrDefault(l.ItemId, string.Empty), l.ReceivedQuantity - l.InvoicedQuantity, l.UnitPrice))];
            Result<MatchInvoiceRequest> mapped = OrdersApiMapping.InvoiceMatch(companyId, order.Id, request, lines);
            return mapped.IsFailure ? Value(mapped) : Value(await procurement.MatchInvoiceAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("MatchPurchaseInvoice");
    }

    private static void MapStock(RouteGroupBuilder api)
    {
        api.MapGet("/api/stock/on-hand", async (long companyId, string? item, string? warehouse, One4AllDbContext db, CancellationToken ct) =>
        {
            string? itemKey = string.IsNullOrWhiteSpace(item) ? null : OrdersApiMapping.Key(item);
            string? warehouseKey = string.IsNullOrWhiteSpace(warehouse) ? null : OrdersApiMapping.Key(warehouse);
            List<StockOnHandApi> rows = await (from b in db.StockBalances.AsNoTracking()
                                               join it in db.Items.AsNoTracking() on b.ItemId equals it.Id
                                               join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
                                               where b.CompanyId == companyId && (itemKey == null || it.Code.ToUpper() == itemKey) && (warehouseKey == null || w.Code.ToUpper() == warehouseKey)
                                                   && (itemKey != null || b.QuantityOnHand != 0m)
                                               orderby it.Code, w.Code
                                               select new StockOnHandApi(it.Code, it.Description, w.Code, b.QuantityOnHand, it.UnitOfMeasure)).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows);
        }).RequireAuthorization().WithName("StockOnHand");

        api.MapGet("/api/stock/valuation", async (long companyId, string? warehouse, One4AllDbContext db, IInventoryService inventory, CancellationToken ct) =>
        {
            long? warehouseId = null;
            if (!string.IsNullOrWhiteSpace(warehouse))
            {
                string key = OrdersApiMapping.Key(warehouse);
                warehouseId = await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId && w.Code.ToUpper() == key).Select(w => (long?)w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (warehouseId is null)
                {
                    return NotFound($"No warehouse {key}.");
                }
            }

            return Results.Ok(await inventory.StockValuationAsync(companyId, warehouseId, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("StockValuation");

        api.MapPost("/api/stock/receipts", async (long companyId, long userId, StockReceiptApi request, One4AllDbContext db, IDataScopeService scopes, IInventoryService inventory, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            (Dictionary<string, ApiItem> items, Dictionary<string, long> warehouses, IResult? refused) = await CodesAsync(db, scopes, companyId, userId, request.ItemCode, [request.WarehouseCode], ct).ConfigureAwait(false);
            if (refused is not null)
            {
                return refused;
            }

            Result<ReceiveStockRequest> mapped = OrdersApiMapping.StockReceipt(companyId, request, items, warehouses);
            return mapped.IsFailure ? Value(mapped) : Value(await inventory.ReceiveAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ReceiveStock");

        api.MapPost("/api/stock/issues", async (long companyId, long userId, StockIssueApi request, One4AllDbContext db, IDataScopeService scopes, IInventoryService inventory, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            (Dictionary<string, ApiItem> items, Dictionary<string, long> warehouses, IResult? refused) = await CodesAsync(db, scopes, companyId, userId, request.ItemCode, [request.WarehouseCode], ct).ConfigureAwait(false);
            if (refused is not null)
            {
                return refused;
            }

            Result<IssueStockRequest> mapped = OrdersApiMapping.StockIssue(companyId, request, items, warehouses);
            return mapped.IsFailure ? Value(mapped) : Value(await inventory.IssueAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("IssueStock");

        api.MapPost("/api/stock/transfers", async (long companyId, long userId, StockTransferApi request, One4AllDbContext db, IDataScopeService scopes, IInventoryService inventory, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            (Dictionary<string, ApiItem> items, Dictionary<string, long> warehouses, IResult? refused) = await CodesAsync(db, scopes, companyId, userId, request.ItemCode,
                [request.FromWarehouseCode, request.ToWarehouseCode], ct).ConfigureAwait(false);
            if (refused is not null)
            {
                return refused;
            }

            Result<StockTransferCommand> mapped = OrdersApiMapping.StockTransfer(companyId, request, items, warehouses);
            if (mapped.IsFailure)
            {
                return Value(mapped);
            }

            StockTransferCommand t = mapped.Value;
            Result<decimal> moved = await inventory.TransferAsync(t.CompanyId, t.ItemId, t.FromWarehouseId, t.ToWarehouseId, t.Date, t.Quantity, t.Reference, ct).ConfigureAwait(false);
            return moved.IsFailure ? Value(moved) : Results.Ok(new { value = moved.Value });
        }).RequireAuthorization().WithName("TransferStock");

        api.MapPost("/api/stock/adjustments", async (long companyId, long userId, StockAdjustmentApi request, One4AllDbContext db, IDataScopeService scopes, IInventoryService inventory, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            (Dictionary<string, ApiItem> items, Dictionary<string, long> warehouses, IResult? refused) = await CodesAsync(db, scopes, companyId, userId, request.ItemCode, [request.WarehouseCode], ct).ConfigureAwait(false);
            if (refused is not null)
            {
                return refused;
            }

            Result<StockAdjustmentCommand> mapped = OrdersApiMapping.StockAdjustment(companyId, request, items, warehouses);
            if (mapped.IsFailure)
            {
                return Value(mapped);
            }

            StockAdjustmentCommand a = mapped.Value;
            Result<decimal> variance = await inventory.AdjustAsync(a.CompanyId, a.ItemId, a.WarehouseId, a.Date, a.CountedQuantity, a.AdjustmentAccountCode, a.Reference, a.ReasonCode, ct).ConfigureAwait(false);
            return variance.IsFailure ? Value(variance) : Results.Ok(new { variance = variance.Value });
        }).RequireAuthorization().WithName("AdjustStock");
    }

    // A status filter: blank for any, else a defined status by name.
    private static bool TryStatus<TStatus>(string? text, out TStatus? status)
        where TStatus : struct, Enum
    {
        status = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Enum.TryParse(text.Trim(), ignoreCase: true, out TStatus parsed) && Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }

        return false;
    }

    private static Task<SalesOrder?> SalesOrderAsync(One4AllDbContext db, long companyId, string number, CancellationToken ct)
    {
        string key = OrdersApiMapping.Key(number);
        return db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber.ToUpper() == key, ct);
    }

    private static Task<PurchaseOrder?> PurchaseOrderAsync(One4AllDbContext db, long companyId, string number, CancellationToken ct)
    {
        string key = OrdersApiMapping.Key(number);
        return db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber.ToUpper() == key, ct);
    }

    private static async Task<Dictionary<long, string>> ItemCodesAsync(One4AllDbContext db, IEnumerable<long> itemIds, CancellationToken ct)
    {
        List<long> ids = [.. itemIds.Distinct()];
        return await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);
    }

    // The items the codes name, keyed by OrdersApiMapping.Key; codes not found are left out (the mapping reports them).
    private static async Task<Dictionary<string, ApiItem>> ItemsAsync(One4AllDbContext db, long companyId, IEnumerable<string?> codes, CancellationToken ct)
    {
        List<string> keys = [.. codes.Select(OrdersApiMapping.Key).Where(k => k.Length > 0).Distinct()];
        var found = await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && keys.Contains(i.Code.ToUpper()))
            .Select(i => new { i.Id, i.Code, i.Description, i.RevenueAccountCode }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, ApiItem> items = new(StringComparer.Ordinal);
        foreach (var i in found)
        {
            items.TryAdd(OrdersApiMapping.Key(i.Code), new ApiItem(i.Id, i.Description, i.RevenueAccountCode));
        }

        return items;
    }

    // A warehouse named by code, held to the user's warehouse scope.
    private static async Task<Result<long>> WarehouseAsync(One4AllDbContext db, IDataScopeService scopes, long companyId, long userId, string code, CancellationToken ct)
    {
        string key = OrdersApiMapping.Key(code);
        long? id = await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId && w.Code.ToUpper() == key).Select(w => (long?)w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null)
        {
            return Error.NotFound("Api.Warehouse", $"No warehouse {key}.");
        }

        return await scopes.AllowsAsync(userId, ScopeDimension.Warehouse, id.Value, ct).ConfigureAwait(false)
            ? id.Value
            : Error.Forbidden("Api.WarehouseScope", $"You have no access to warehouse {key}.");
    }

    // The item and warehouses a stock movement names; unknown codes are left for the mapping to report, a warehouse outside the user's scope is refused.
    private static async Task<(Dictionary<string, ApiItem> Items, Dictionary<string, long> Warehouses, IResult? Refused)> CodesAsync(One4AllDbContext db, IDataScopeService scopes, long companyId, long userId,
        string itemCode, IReadOnlyList<string> warehouseCodes, CancellationToken ct)
    {
        Dictionary<string, ApiItem> items = await ItemsAsync(db, companyId, [itemCode], ct).ConfigureAwait(false);
        Dictionary<string, long> warehouses = new(StringComparer.Ordinal);
        foreach (string code in warehouseCodes.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            Result<long> warehouse = await WarehouseAsync(db, scopes, companyId, userId, code, ct).ConfigureAwait(false);
            if (warehouse.IsSuccess)
            {
                warehouses[OrdersApiMapping.Key(code)] = warehouse.Value;
            }
            else if (warehouse.Error.Type == ErrorType.Forbidden)
            {
                return (items, warehouses, Value(warehouse));
            }
        }

        return (items, warehouses, null);
    }
}
