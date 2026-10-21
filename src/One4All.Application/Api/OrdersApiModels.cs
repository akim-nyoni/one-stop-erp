using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Api;

/// <summary>A line of a new sales order as the REST API takes it (brief §11). Quantities and prices are in the item's own unit unless a unit is given.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price; null takes the customer's price list (then the default list).</param>
/// <param name="Description">The description (blank: the item's).</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="RevenueAccountCode">The revenue account (blank: the item's).</param>
/// <param name="UnitCode">The unit the quantity and price are in (blank: the item's own unit).</param>
public sealed record SalesOrderLineApi(string ItemCode, decimal Quantity, decimal? UnitPrice = null, string? Description = null, string? TaxCode = null, string? RevenueAccountCode = null, string? UnitCode = null);

/// <summary>A sales order as the REST API creates it.</summary>
/// <param name="CustomerCode">The customer code.</param>
/// <param name="OrderDate">The order date.</param>
/// <param name="Lines">The lines.</param>
/// <param name="OrderNumber">The order number (blank: the next in the company's SO sequence).</param>
/// <param name="Currency">The ISO currency (blank: the customer's).</param>
/// <param name="RequestedDeliveryDate">The date the customer wants the goods.</param>
public sealed record SalesOrderApi(string CustomerCode, DateOnly OrderDate, IReadOnlyList<SalesOrderLineApi> Lines, string? OrderNumber = null, string? Currency = null, DateOnly? RequestedDeliveryDate = null);

/// <summary>
/// A line picked from an existing order by item code: what to despatch, invoice, receive or match. A null quantity takes all that is outstanding
/// on the line for the step; quantities are in the item's own unit.
/// </summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Quantity">The quantity (null: all outstanding for the step).</param>
/// <param name="UnitPrice">The invoiced unit price (purchase invoice match; null: the order price).</param>
/// <param name="TaxAmount">The tax on the quantity (sales order invoice; null: the line's tax code rate).</param>
public sealed record OrderLineApi(string ItemCode, decimal? Quantity = null, decimal? UnitPrice = null, decimal? TaxAmount = null);

/// <summary>A despatch of a sales order as the REST API posts it.</summary>
/// <param name="WarehouseCode">The warehouse despatched from.</param>
/// <param name="Date">The despatch date.</param>
/// <param name="Lines">The lines (blank: everything still to despatch).</param>
/// <param name="Reference">The delivery note reference (blank: DN-{order}-{yyyyMMdd}).</param>
/// <param name="PlannedFor">The date the delivery is planned for.</param>
/// <param name="Vehicle">The vehicle.</param>
/// <param name="Route">The route.</param>
/// <param name="Driver">The driver.</param>
public sealed record DespatchApi(string WarehouseCode, DateOnly Date, IReadOnlyList<OrderLineApi>? Lines = null, string? Reference = null, DateOnly? PlannedFor = null, string? Vehicle = null, string? Route = null,
    string? Driver = null);

/// <summary>An invoice of a sales order's despatched goods as the REST API posts it.</summary>
/// <param name="Date">The invoice date.</param>
/// <param name="Lines">The lines (blank: everything despatched and not yet invoiced).</param>
/// <param name="InvoiceNumber">The invoice number (blank: INV-{order}-{yyyyMMdd}).</param>
/// <param name="TaxAccountCode">The output tax account (blank: the company's).</param>
public sealed record SalesOrderInvoiceApi(DateOnly Date, IReadOnlyList<OrderLineApi>? Lines = null, string? InvoiceNumber = null, string? TaxAccountCode = null);

/// <summary>A line of a new purchase order as the REST API takes it.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Description">The description (blank: the item's).</param>
/// <param name="ExpenseAccountCode">The expense account, for a non-stock line.</param>
/// <param name="UnitCode">The unit the quantity and price are in (blank: the item's own unit).</param>
public sealed record PurchaseOrderLineApi(string ItemCode, decimal Quantity, decimal UnitPrice, string? Description = null, string? ExpenseAccountCode = null, string? UnitCode = null);

/// <summary>A purchase order as the REST API creates it.</summary>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="OrderDate">The order date.</param>
/// <param name="Lines">The lines.</param>
/// <param name="OrderNumber">The order number (blank: the next in the company's PO sequence).</param>
/// <param name="Currency">The ISO currency (blank: the supplier's).</param>
/// <param name="RequiredBy">When the goods are required.</param>
/// <param name="Department">The department the order is for.</param>
/// <param name="CostCentre">The cost centre the order is for.</param>
/// <param name="Project">The project the order is for.</param>
public sealed record PurchaseOrderApi(string SupplierCode, DateOnly OrderDate, IReadOnlyList<PurchaseOrderLineApi> Lines, string? OrderNumber = null, string? Currency = null, DateOnly? RequiredBy = null,
    string? Department = null, string? CostCentre = null, string? Project = null);

/// <summary>A goods received note against a purchase order as the REST API posts it.</summary>
/// <param name="WarehouseCode">The warehouse the goods are received into.</param>
/// <param name="Date">The receipt date.</param>
/// <param name="Lines">The lines (blank: everything still outstanding).</param>
/// <param name="GrnNumber">The GRN number (blank: the next in the company's GRN sequence).</param>
public sealed record GoodsReceiptApi(string WarehouseCode, DateOnly Date, IReadOnlyList<OrderLineApi>? Lines = null, string? GrnNumber = null);

/// <summary>A supplier invoice matched against a purchase order (three-way) as the REST API posts it.</summary>
/// <param name="OrderNumber">The purchase order number.</param>
/// <param name="InvoiceNumber">The supplier's invoice number.</param>
/// <param name="Date">The invoice date.</param>
/// <param name="Lines">The lines (blank: everything received and not yet invoiced, at the order price).</param>
/// <param name="TolerancePercent">A price tolerance that may only tighten the supplier's or company's rule.</param>
/// <param name="PriceVarianceAccountCode">The price variance account, when the invoice price differs from the order's.</param>
public sealed record InvoiceMatchApi(string OrderNumber, string InvoiceNumber, DateOnly Date, IReadOnlyList<OrderLineApi>? Lines = null, decimal? TolerancePercent = null, string? PriceVarianceAccountCode = null);

/// <summary>A stock receipt as the REST API posts it (not against an order: opening stock, a found item, a return from use).</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="WarehouseCode">The warehouse code.</param>
/// <param name="Date">The movement date.</param>
/// <param name="Quantity">The quantity received.</param>
/// <param name="UnitCost">The unit cost.</param>
/// <param name="CreditAccountCode">The account credited (opening balances, a clearing account).</param>
/// <param name="Reference">The reference or reason.</param>
public sealed record StockReceiptApi(string ItemCode, string WarehouseCode, DateOnly Date, decimal Quantity, decimal UnitCost, string CreditAccountCode, string? Reference = null);

/// <summary>A stock issue as the REST API posts it (at moving-average cost; an over-issue is refused).</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="WarehouseCode">The warehouse code.</param>
/// <param name="Date">The movement date.</param>
/// <param name="Quantity">The quantity issued.</param>
/// <param name="Reference">The reference or reason.</param>
/// <param name="ChargeAccountCode">The account charged, for internal use (blank: the item's cost of sales).</param>
/// <param name="CostCentre">The cost centre the charge is analysed to.</param>
public sealed record StockIssueApi(string ItemCode, string WarehouseCode, DateOnly Date, decimal Quantity, string? Reference = null, string? ChargeAccountCode = null, string? CostCentre = null);

/// <summary>A stock transfer between two warehouses as the REST API posts it.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="FromWarehouseCode">The source warehouse code.</param>
/// <param name="ToWarehouseCode">The destination warehouse code.</param>
/// <param name="Date">The transfer date.</param>
/// <param name="Quantity">The quantity moved.</param>
/// <param name="Reference">The reference or reason.</param>
public sealed record StockTransferApi(string ItemCode, string FromWarehouseCode, string ToWarehouseCode, DateOnly Date, decimal Quantity, string? Reference = null);

/// <summary>A stock adjustment to a physical count as the REST API posts it.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="WarehouseCode">The warehouse counted.</param>
/// <param name="Date">The count date.</param>
/// <param name="CountedQuantity">The quantity counted (the new on-hand).</param>
/// <param name="AdjustmentAccountCode">The stock adjustment account taking the variance.</param>
/// <param name="Reference">The reference (a stocktake number).</param>
/// <param name="ReasonCode">The reason code (damaged, expired, theft...).</param>
public sealed record StockAdjustmentApi(string ItemCode, string WarehouseCode, DateOnly Date, decimal CountedQuantity, string AdjustmentAccountCode, string? Reference = null, string? ReasonCode = null);

/// <summary>A customer or supplier found by its code.</summary>
/// <param name="Id">The id.</param>
/// <param name="Currency">Its currency.</param>
public readonly record struct ApiParty(long Id, CurrencyCode Currency);

/// <summary>An item found by its code.</summary>
/// <param name="Id">The id.</param>
/// <param name="Description">The description.</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
public readonly record struct ApiItem(long Id, string Description, string? RevenueAccountCode = null);

/// <summary>A line of an existing order as a step sees it: what is outstanding for that step (to despatch, invoice, receive or match).</summary>
/// <param name="LineId">The order line.</param>
/// <param name="ItemCode">The item code.</param>
/// <param name="Outstanding">The quantity outstanding for the step.</param>
/// <param name="UnitPrice">The order unit price.</param>
/// <param name="TaxRatePercent">The line's tax rate on the step's date (sales order invoice).</param>
public readonly record struct ApiOrderLine(long LineId, string ItemCode, decimal Outstanding, decimal UnitPrice, decimal TaxRatePercent = 0m);

/// <summary>A stock transfer with its codes resolved.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="ItemId">The item.</param>
/// <param name="FromWarehouseId">The source warehouse.</param>
/// <param name="ToWarehouseId">The destination warehouse.</param>
/// <param name="Date">The date.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Reference">The reference.</param>
public readonly record struct StockTransferCommand(long CompanyId, long ItemId, long FromWarehouseId, long ToWarehouseId, DateOnly Date, decimal Quantity, string? Reference);

/// <summary>A stock adjustment with its codes resolved.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="ItemId">The item.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Date">The date.</param>
/// <param name="CountedQuantity">The counted quantity.</param>
/// <param name="AdjustmentAccountCode">The adjustment account.</param>
/// <param name="Reference">The reference.</param>
/// <param name="ReasonCode">The reason code.</param>
public readonly record struct StockAdjustmentCommand(long CompanyId, long ItemId, long WarehouseId, DateOnly Date, decimal CountedQuantity, string AdjustmentAccountCode, string? Reference, string? ReasonCode);

/// <summary>A sales order in a list.</summary>
/// <param name="Number">The order number.</param>
/// <param name="CustomerCode">The customer code.</param>
/// <param name="Date">The order date.</param>
/// <param name="Status">The status.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Total">The net total.</param>
public sealed record SalesOrderRowApi(string Number, string CustomerCode, DateOnly Date, string Status, string Currency, decimal Total);

/// <summary>A sales order line as the REST API reads it.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The description.</param>
/// <param name="Ordered">The quantity ordered (item's own unit).</param>
/// <param name="Despatched">The quantity despatched.</param>
/// <param name="Invoiced">The quantity invoiced.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
/// <param name="UnitCode">The unit it was ordered in, if not the item's own.</param>
public sealed record SalesOrderLineViewApi(string ItemCode, string Description, decimal Ordered, decimal Despatched, decimal Invoiced, decimal UnitPrice, string? TaxCode, string RevenueAccountCode, string? UnitCode);

/// <summary>A sales order as the REST API reads it.</summary>
/// <param name="Number">The order number.</param>
/// <param name="CustomerCode">The customer code.</param>
/// <param name="Date">The order date.</param>
/// <param name="Status">The status.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Total">The net total.</param>
/// <param name="RequestedDeliveryDate">The requested delivery date.</param>
/// <param name="Lines">The lines.</param>
public sealed record SalesOrderViewApi(string Number, string CustomerCode, DateOnly Date, string Status, string Currency, decimal Total, DateOnly? RequestedDeliveryDate, IReadOnlyList<SalesOrderLineViewApi> Lines);

/// <summary>A purchase order in a list.</summary>
/// <param name="Number">The order number.</param>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="Date">The order date.</param>
/// <param name="Status">The status.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Total">The net total.</param>
public sealed record PurchaseOrderRowApi(string Number, string SupplierCode, DateOnly Date, string Status, string Currency, decimal Total);

/// <summary>A purchase order line as the REST API reads it.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The description.</param>
/// <param name="Ordered">The quantity ordered (item's own unit).</param>
/// <param name="Received">The quantity received.</param>
/// <param name="Invoiced">The quantity invoiced.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="UnitCode">The unit it was ordered in, if not the item's own.</param>
public sealed record PurchaseOrderLineViewApi(string ItemCode, string Description, decimal Ordered, decimal Received, decimal Invoiced, decimal UnitPrice, string? UnitCode);

/// <summary>A purchase order as the REST API reads it.</summary>
/// <param name="Number">The order number.</param>
/// <param name="SupplierCode">The supplier code.</param>
/// <param name="Date">The order date.</param>
/// <param name="Status">The status.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Total">The net total.</param>
/// <param name="RequiredBy">When the goods are required.</param>
/// <param name="AwaitingApproval">Whether it is waiting for approval.</param>
/// <param name="Lines">The lines.</param>
public sealed record PurchaseOrderViewApi(string Number, string SupplierCode, DateOnly Date, string Status, string Currency, decimal Total, DateOnly? RequiredBy, bool AwaitingApproval,
    IReadOnlyList<PurchaseOrderLineViewApi> Lines);

/// <summary>Stock on hand of an item in a warehouse.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The item description.</param>
/// <param name="WarehouseCode">The warehouse code.</param>
/// <param name="Quantity">The quantity on hand.</param>
/// <param name="Unit">The item's unit of measure.</param>
public sealed record StockOnHandApi(string ItemCode, string Description, string WarehouseCode, decimal Quantity, string Unit);

/// <summary>
/// Turns the REST API's order and stock documents into the application's requests: codes resolved (from lookups the caller loads), lines
/// picked from the order by item code, currencies parsed, the company and acting user stamped on, bad input refused. Pure.
/// </summary>
public static class OrdersApiMapping
{
    /// <summary>The key a code is looked up by (trimmed, upper case).</summary>
    /// <param name="code">The code.</param>
    /// <returns>The key.</returns>
    public static string Key(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>A sales order request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">Who raises it.</param>
    /// <param name="api">The order as posted.</param>
    /// <param name="customer">The customer named by its code.</param>
    /// <param name="items">The items by <see cref="Key"/>.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<CreateSalesOrderRequest> SalesOrder(long companyId, long userId, SalesOrderApi api, ApiParty customer, IReadOnlyDictionary<string, ApiItem> items)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(items);
        if (api.Lines is null || api.Lines.Count == 0)
        {
            return Error.Validation("Api.Lines", "An order needs at least one line.");
        }

        Result<CurrencyCode> currency = string.IsNullOrWhiteSpace(api.Currency) ? customer.Currency : TradingApiMapping.Currency(api.Currency);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        List<SalesOrderLineRequest> lines = [];
        foreach (SalesOrderLineApi line in api.Lines)
        {
            if (!items.TryGetValue(Key(line.ItemCode), out ApiItem item))
            {
                return UnknownItem(line.ItemCode);
            }

            if (line.Quantity <= 0m)
            {
                return Error.Validation("Api.Quantity", $"The quantity of {Key(line.ItemCode)} must be more than zero.");
            }

            string? revenue = Clean(line.RevenueAccountCode) ?? Clean(item.RevenueAccountCode);
            if (revenue is null)
            {
                return Error.Validation("Api.RevenueAccount", $"Item {Key(line.ItemCode)} has no revenue account; give one.");
            }

            lines.Add(new SalesOrderLineRequest(item.Id, Clean(line.Description) ?? item.Description, line.Quantity, line.UnitPrice, Upper(line.TaxCode), revenue, Upper(line.UnitCode)));
        }

        return new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = Upper(api.OrderNumber) ?? string.Empty, OrderDate = api.OrderDate, Currency = currency.Value, Lines = lines,
            RequestedDeliveryDate = api.RequestedDeliveryDate, RaisedByUserId = userId,
        };
    }

    /// <summary>A despatch request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The order.</param>
    /// <param name="orderNumber">The order number (for messages and the default reference).</param>
    /// <param name="api">The despatch as posted.</param>
    /// <param name="warehouseId">The warehouse named by its code.</param>
    /// <param name="lines">The order's lines, outstanding = still to despatch.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<DespatchRequest> Despatch(long companyId, long salesOrderId, string orderNumber, DespatchApi api, long warehouseId, IReadOnlyList<ApiOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<List<PickedLine>> picked = Pick(orderNumber, lines, api.Lines, "left to despatch");
        return picked.IsFailure ? picked.Error : new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = salesOrderId, WarehouseId = warehouseId, Date = api.Date, Reference = Clean(api.Reference) ?? $"DN-{orderNumber}-{api.Date:yyyyMMdd}",
            Lines = [.. picked.Value.Select(p => new DespatchLine(p.Line.LineId, p.Quantity))], PlannedFor = api.PlannedFor, Vehicle = Clean(api.Vehicle), Route = Clean(api.Route), Driver = Clean(api.Driver),
        };
    }

    /// <summary>A sales order invoice request; a line without a tax amount is taxed at its tax code's rate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The order.</param>
    /// <param name="orderNumber">The order number (for messages and the default invoice number).</param>
    /// <param name="api">The invoice as posted.</param>
    /// <param name="lines">The order's lines, outstanding = despatched and not yet invoiced, with their tax rates on the invoice date.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<InvoiceOrderRequest> SalesOrderInvoice(long companyId, long salesOrderId, string orderNumber, SalesOrderInvoiceApi api, IReadOnlyList<ApiOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<List<PickedLine>> picked = Pick(orderNumber, lines, api.Lines, "despatched and waiting to be invoiced");
        return picked.IsFailure ? picked.Error : new InvoiceOrderRequest
        {
            CompanyId = companyId, SalesOrderId = salesOrderId, InvoiceNumber = Upper(api.InvoiceNumber) ?? $"INV-{orderNumber}-{api.Date:yyyyMMdd}", Date = api.Date, TaxAccountCode = Clean(api.TaxAccountCode),
            Lines = [.. picked.Value.Select(p => new InvoiceOrderLine(p.Line.LineId, p.Quantity,
                p.Given?.TaxAmount ?? decimal.Round(p.Quantity * p.Line.UnitPrice * p.Line.TaxRatePercent / 100m, 2, MidpointRounding.AwayFromZero)))],
        };
    }

    /// <summary>A purchase order request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">Who raises it.</param>
    /// <param name="api">The order as posted.</param>
    /// <param name="supplier">The supplier named by its code.</param>
    /// <param name="items">The items by <see cref="Key"/>.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<CreatePurchaseOrderRequest> PurchaseOrder(long companyId, long userId, PurchaseOrderApi api, ApiParty supplier, IReadOnlyDictionary<string, ApiItem> items)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(items);
        if (api.Lines is null || api.Lines.Count == 0)
        {
            return Error.Validation("Api.Lines", "An order needs at least one line.");
        }

        Result<CurrencyCode> currency = string.IsNullOrWhiteSpace(api.Currency) ? supplier.Currency : TradingApiMapping.Currency(api.Currency);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        List<PurchaseOrderLineRequest> lines = [];
        foreach (PurchaseOrderLineApi line in api.Lines)
        {
            if (!items.TryGetValue(Key(line.ItemCode), out ApiItem item))
            {
                return UnknownItem(line.ItemCode);
            }

            if (line.Quantity <= 0m || line.UnitPrice < 0m)
            {
                return Error.Validation("Api.Quantity", $"The quantity of {Key(line.ItemCode)} must be more than zero and its price not negative.");
            }

            lines.Add(new PurchaseOrderLineRequest(item.Id, Clean(line.Description) ?? item.Description, line.Quantity, line.UnitPrice, Clean(line.ExpenseAccountCode), UnitCode: Upper(line.UnitCode)));
        }

        return new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = Upper(api.OrderNumber) ?? string.Empty, OrderDate = api.OrderDate, RequiredBy = api.RequiredBy, Department = Clean(api.Department),
            CostCentre = Clean(api.CostCentre), Project = Clean(api.Project), Currency = currency.Value, Lines = lines, RaisedByUserId = userId,
        };
    }

    /// <summary>A goods received note request (the GRN number as given; the caller allocates one first when it is blank).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">Who receives.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="orderNumber">The order number (for messages).</param>
    /// <param name="api">The receipt as posted.</param>
    /// <param name="warehouseId">The warehouse named by its code.</param>
    /// <param name="lines">The order's lines, outstanding = still to receive.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<ReceiveGoodsRequest> GoodsReceipt(long companyId, long userId, long purchaseOrderId, string orderNumber, GoodsReceiptApi api, long warehouseId, IReadOnlyList<ApiOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<List<PickedLine>> picked = Pick(orderNumber, lines, api.Lines, "left to receive");
        return picked.IsFailure ? picked.Error : new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = purchaseOrderId, WarehouseId = warehouseId, GrnNumber = Upper(api.GrnNumber) ?? string.Empty, Date = api.Date,
            Lines = [.. picked.Value.Select(p => new ReceiveLine(p.Line.LineId, p.Quantity))], ReceivedByUserId = userId,
        };
    }

    /// <summary>A three-way invoice match request; a line without a price is invoiced at the order price.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="api">The invoice as posted.</param>
    /// <param name="lines">The order's lines, outstanding = received and not yet invoiced.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<MatchInvoiceRequest> InvoiceMatch(long companyId, long purchaseOrderId, InvoiceMatchApi api, IReadOnlyList<ApiOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (string.IsNullOrWhiteSpace(api.InvoiceNumber))
        {
            return Error.Validation("Api.InvoiceNumber", "Give the supplier's invoice number.");
        }

        Result<List<PickedLine>> picked = Pick(Key(api.OrderNumber), lines, api.Lines, "received and waiting to be invoiced");
        return picked.IsFailure ? picked.Error : new MatchInvoiceRequest
        {
            CompanyId = companyId, PurchaseOrderId = purchaseOrderId, InvoiceNumber = api.InvoiceNumber.Trim(), Date = api.Date, TolerancePercent = api.TolerancePercent,
            PriceVarianceAccountCode = Clean(api.PriceVarianceAccountCode), Lines = [.. picked.Value.Select(p => new InvoiceMatchLine(p.Line.LineId, p.Quantity, p.Given?.UnitPrice ?? p.Line.UnitPrice))],
        };
    }

    /// <summary>A stock receipt request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The receipt as posted.</param>
    /// <param name="items">The items by <see cref="Key"/>.</param>
    /// <param name="warehouses">The warehouse ids by <see cref="Key"/>.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<ReceiveStockRequest> StockReceipt(long companyId, StockReceiptApi api, IReadOnlyDictionary<string, ApiItem> items, IReadOnlyDictionary<string, long> warehouses)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<(long Item, long Warehouse)> found = Resolve(api.ItemCode, api.WarehouseCode, items, warehouses);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (api.Quantity <= 0m || api.UnitCost < 0m)
        {
            return Error.Validation("Api.Quantity", "The quantity must be more than zero and the cost not negative.");
        }

        return Clean(api.CreditAccountCode) is not string credit
            ? Error.Validation("Api.CreditAccount", "Give the account the receipt is credited to.")
            : new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = found.Value.Item, WarehouseId = found.Value.Warehouse, Date = api.Date, Quantity = api.Quantity, UnitCost = api.UnitCost, CreditAccountCode = credit,
                Reference = Clean(api.Reference),
            };
    }

    /// <summary>A stock issue request (never past zero: an over-issue is refused).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The issue as posted.</param>
    /// <param name="items">The items by <see cref="Key"/>.</param>
    /// <param name="warehouses">The warehouse ids by <see cref="Key"/>.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<IssueStockRequest> StockIssue(long companyId, StockIssueApi api, IReadOnlyDictionary<string, ApiItem> items, IReadOnlyDictionary<string, long> warehouses)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<(long Item, long Warehouse)> found = Resolve(api.ItemCode, api.WarehouseCode, items, warehouses);
        if (found.IsFailure)
        {
            return found.Error;
        }

        return api.Quantity <= 0m
            ? Error.Validation("Api.Quantity", "The quantity must be more than zero.")
            : new IssueStockRequest
            {
                CompanyId = companyId, ItemId = found.Value.Item, WarehouseId = found.Value.Warehouse, Date = api.Date, Quantity = api.Quantity, Reference = Clean(api.Reference),
                ChargeAccountCode = Clean(api.ChargeAccountCode), CostCentre = Clean(api.CostCentre),
            };
    }

    /// <summary>A stock transfer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The transfer as posted.</param>
    /// <param name="items">The items by <see cref="Key"/>.</param>
    /// <param name="warehouses">The warehouse ids by <see cref="Key"/>.</param>
    /// <returns>The transfer, or why not.</returns>
    public static Result<StockTransferCommand> StockTransfer(long companyId, StockTransferApi api, IReadOnlyDictionary<string, ApiItem> items, IReadOnlyDictionary<string, long> warehouses)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<(long Item, long Warehouse)> from = Resolve(api.ItemCode, api.FromWarehouseCode, items, warehouses);
        if (from.IsFailure)
        {
            return from.Error;
        }

        Result<(long Item, long Warehouse)> to = Resolve(api.ItemCode, api.ToWarehouseCode, items, warehouses);
        if (to.IsFailure)
        {
            return to.Error;
        }

        if (from.Value.Warehouse == to.Value.Warehouse)
        {
            return Error.Validation("Api.Warehouse", "A transfer needs two different warehouses.");
        }

        return api.Quantity <= 0m
            ? Error.Validation("Api.Quantity", "The quantity must be more than zero.")
            : new StockTransferCommand(companyId, from.Value.Item, from.Value.Warehouse, to.Value.Warehouse, api.Date, api.Quantity, Clean(api.Reference));
    }

    /// <summary>A stock adjustment to a count.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="api">The adjustment as posted.</param>
    /// <param name="items">The items by <see cref="Key"/>.</param>
    /// <param name="warehouses">The warehouse ids by <see cref="Key"/>.</param>
    /// <returns>The adjustment, or why not.</returns>
    public static Result<StockAdjustmentCommand> StockAdjustment(long companyId, StockAdjustmentApi api, IReadOnlyDictionary<string, ApiItem> items, IReadOnlyDictionary<string, long> warehouses)
    {
        ArgumentNullException.ThrowIfNull(api);
        Result<(long Item, long Warehouse)> found = Resolve(api.ItemCode, api.WarehouseCode, items, warehouses);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (api.CountedQuantity < 0m)
        {
            return Error.Validation("Api.Quantity", "The counted quantity cannot be negative.");
        }

        return Clean(api.AdjustmentAccountCode) is not string account
            ? Error.Validation("Api.AdjustmentAccount", "Give the stock adjustment account.")
            : new StockAdjustmentCommand(companyId, found.Value.Item, found.Value.Warehouse, api.Date, api.CountedQuantity, account, Clean(api.Reference), Upper(api.ReasonCode));
    }

    // The order lines a step works on: every line with something outstanding, or the lines given by item code (a line with something
    // outstanding first), each for its given quantity or all that is outstanding on it.
    private static Result<List<PickedLine>> Pick(string orderNumber, IReadOnlyList<ApiOrderLine> lines, IReadOnlyList<OrderLineApi>? given, string what)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<PickedLine> picked = [];
        if (given is null || given.Count == 0)
        {
            picked.AddRange(lines.Where(l => l.Outstanding > 0m).Select(l => new PickedLine(l, null, l.Outstanding)));
            return picked.Count == 0 ? Error.Rule("Api.NothingOutstanding", $"Nothing on order {orderNumber} is {what}.") : picked;
        }

        foreach (OrderLineApi line in given)
        {
            string code = Key(line.ItemCode);
            List<ApiOrderLine> matches = [.. lines.Where(l => Key(l.ItemCode) == code)];
            if (matches.Count == 0)
            {
                return Error.Validation("Api.OrderLine", $"Order {orderNumber} has no line for {code}.");
            }

            int open = matches.FindIndex(l => l.Outstanding > 0m);
            ApiOrderLine found = matches[Math.Max(0, open)];
            decimal quantity = line.Quantity ?? found.Outstanding;
            if (quantity <= 0m)
            {
                return Error.Validation("Api.Quantity", $"Nothing of {code} on order {orderNumber} is {what}.");
            }

            picked.Add(new PickedLine(found, line, quantity));
        }

        return picked;
    }

    private static Result<(long Item, long Warehouse)> Resolve(string itemCode, string warehouseCode, IReadOnlyDictionary<string, ApiItem> items, IReadOnlyDictionary<string, long> warehouses)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(warehouses);
        if (!items.TryGetValue(Key(itemCode), out ApiItem item))
        {
            return UnknownItem(itemCode);
        }

        return warehouses.TryGetValue(Key(warehouseCode), out long warehouse)
            ? (item.Id, warehouse)
            : Error.NotFound("Api.Warehouse", $"No warehouse {Key(warehouseCode)}.");
    }

    private readonly record struct PickedLine(ApiOrderLine Line, OrderLineApi? Given, decimal Quantity);

    private static Error UnknownItem(string? code) => Error.NotFound("Api.Item", $"No item {Key(code)}.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Upper(string? value) => Clean(value)?.ToUpperInvariant();
}
