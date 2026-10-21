using FluentAssertions;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Api;
using One4All.Core.Accounting;
using One4All.Core.Common;
using Xunit;

namespace One4All.UnitTests.Api;

/// <summary>The REST API's order and stock documents map onto the application's requests: codes resolved, order lines picked by item code, bad input refused.</summary>
public sealed class OrdersApiMappingTests
{
    private static readonly Dictionary<string, ApiItem> Items = new()
    {
        ["WIDGET"] = new ApiItem(10, "Widget", "4000"),
        ["BOLT"] = new ApiItem(11, "Bolt"),
    };

    private static readonly Dictionary<string, long> Warehouses = new() { ["MAIN"] = 1, ["SHOP"] = 2 };

    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void Sales_orders_resolve_items_take_the_customers_currency_and_need_a_revenue_account()
    {
        CurrencyCode usd = CurrencyCode.Parse("USD");
        SalesOrderApi api = new(" cust1 ", Day, [new SalesOrderLineApi(" widget ", 3m, 5m, TaxCode: "vat")], RequestedDeliveryDate: Day.AddDays(7));

        CreateSalesOrderRequest request = OrdersApiMapping.SalesOrder(3, 8, api, new ApiParty(7, usd), Items).Value;
        request.Should().BeEquivalentTo(new { CompanyId = 3L, CustomerId = 7L, OrderNumber = string.Empty, RaisedByUserId = (long?)8L, RequestedDeliveryDate = (DateOnly?)Day.AddDays(7) });
        request.Currency.Should().Be(usd);
        request.Lines.Should().ContainSingle().Which.Should().Be(new SalesOrderLineRequest(10, "Widget", 3m, 5m, "VAT", "4000"));

        OrdersApiMapping.SalesOrder(3, 8, api with { Currency = "eur" }, new ApiParty(7, usd), Items).Value.Currency.Value.Should().Be("EUR");
        OrdersApiMapping.SalesOrder(3, 8, api with { Lines = [new SalesOrderLineApi("BOLT", 1m)] }, new ApiParty(7, usd), Items).Error.Code.Should().Be("Api.RevenueAccount");
        OrdersApiMapping.SalesOrder(3, 8, api with { Lines = [new SalesOrderLineApi("NOPE", 1m)] }, new ApiParty(7, usd), Items).Error.Type.Should().Be(ErrorType.NotFound);
        OrdersApiMapping.SalesOrder(3, 8, api with { Lines = [new SalesOrderLineApi("WIDGET", 0m)] }, new ApiParty(7, usd), Items).Error.Code.Should().Be("Api.Quantity");
        OrdersApiMapping.SalesOrder(3, 8, api with { Lines = [] }, new ApiParty(7, usd), Items).Error.Code.Should().Be("Api.Lines");
    }

    [Fact]
    public void Despatch_and_invoice_pick_order_lines_by_item_code()
    {
        List<ApiOrderLine> toDespatch = [new(100, "WIDGET", 4m, 5m), new(101, "BOLT", 0m, 1m)];
        DespatchRequest all = OrdersApiMapping.Despatch(3, 50, "SO-1", new DespatchApi("MAIN", Day, Driver: "Ann"), 1, toDespatch).Value;
        all.Lines.Should().Equal(new DespatchLine(100, 4m));
        all.Reference.Should().Be("DN-SO-1-20261001");
        all.Should().BeEquivalentTo(new { CompanyId = 3L, SalesOrderId = 50L, WarehouseId = 1L, Driver = "Ann" });

        OrdersApiMapping.Despatch(3, 50, "SO-1", new DespatchApi("MAIN", Day, [new OrderLineApi("widget", 1.5m)], "DN-9"), 1, toDespatch).Value.Lines.Should().Equal(new DespatchLine(100, 1.5m));
        OrdersApiMapping.Despatch(3, 50, "SO-1", new DespatchApi("MAIN", Day, [new OrderLineApi("NUT")]), 1, toDespatch).Error.Code.Should().Be("Api.OrderLine");
        OrdersApiMapping.Despatch(3, 50, "SO-1", new DespatchApi("MAIN", Day, [new OrderLineApi("BOLT")]), 1, toDespatch).Error.Code.Should().Be("Api.Quantity");
        OrdersApiMapping.Despatch(3, 50, "SO-1", new DespatchApi("MAIN", Day), 1, [new(100, "WIDGET", 0m, 5m)]).Error.Type.Should().Be(ErrorType.Rule);

        List<ApiOrderLine> toInvoice = [new(100, "WIDGET", 4m, 5m, 15m), new(101, "BOLT", 2m, 1m)];
        InvoiceOrderRequest invoice = OrdersApiMapping.SalesOrderInvoice(3, 50, "SO-1", new SalesOrderInvoiceApi(Day), toInvoice).Value;
        invoice.InvoiceNumber.Should().Be("INV-SO-1-20261001");
        invoice.Lines.Should().Equal(new InvoiceOrderLine(100, 4m, 3m), new InvoiceOrderLine(101, 2m, 0m));
        OrdersApiMapping.SalesOrderInvoice(3, 50, "SO-1", new SalesOrderInvoiceApi(Day, [new OrderLineApi("WIDGET", 2m, TaxAmount: 1.25m)], "inv-7", "2200"), toInvoice).Value
            .Should().BeEquivalentTo(new { InvoiceNumber = "INV-7", TaxAccountCode = "2200", Lines = new[] { new InvoiceOrderLine(100, 2m, 1.25m) } });
    }

    [Fact]
    public void Purchase_orders_receipts_and_invoice_matches_map()
    {
        CurrencyCode zar = CurrencyCode.Parse("ZAR");
        PurchaseOrderApi api = new("SUP1", Day, [new PurchaseOrderLineApi("BOLT", 100m, 0.5m, UnitCode: "box")], Department: "OPS");
        CreatePurchaseOrderRequest order = OrdersApiMapping.PurchaseOrder(3, 8, api, new ApiParty(9, zar), Items).Value;
        order.Should().BeEquivalentTo(new { CompanyId = 3L, SupplierId = 9L, RaisedByUserId = 8L, Department = "OPS" });
        order.Currency.Should().Be(zar);
        order.Lines.Should().Equal(new PurchaseOrderLineRequest(11, "Bolt", 100m, 0.5m, UnitCode: "BOX"));
        OrdersApiMapping.PurchaseOrder(3, 8, api with { Lines = [new PurchaseOrderLineApi("BOLT", 1m, -1m)] }, new ApiParty(9, zar), Items).Error.Code.Should().Be("Api.Quantity");
        OrdersApiMapping.PurchaseOrder(3, 8, api with { Currency = "rand" }, new ApiParty(9, zar), Items).Error.Code.Should().Be("Api.Currency");

        List<ApiOrderLine> toReceive = [new(200, "BOLT", 60m, 0.5m)];
        ReceiveGoodsRequest grn = OrdersApiMapping.GoodsReceipt(3, 8, 70, "PO-1", new GoodsReceiptApi("MAIN", Day), 1, toReceive).Value;
        grn.Should().BeEquivalentTo(new { CompanyId = 3L, PurchaseOrderId = 70L, WarehouseId = 1L, GrnNumber = string.Empty, ReceivedByUserId = 8L });
        grn.Lines.Should().ContainSingle().Which.Should().BeEquivalentTo(new { PurchaseOrderLineId = 200L, Quantity = 60m });
        OrdersApiMapping.GoodsReceipt(3, 8, 70, "PO-1", new GoodsReceiptApi("MAIN", Day, [new OrderLineApi("BOLT", 10m)], "grn-5"), 1, toReceive).Value.GrnNumber.Should().Be("GRN-5");

        List<ApiOrderLine> toMatch = [new(200, "BOLT", 40m, 0.5m)];
        MatchInvoiceRequest match = OrdersApiMapping.InvoiceMatch(3, 70, new InvoiceMatchApi("PO-1", "SI-1", Day, [new OrderLineApi("BOLT", 40m, 0.55m)], 2m, "5100"), toMatch).Value;
        match.Should().BeEquivalentTo(new { CompanyId = 3L, PurchaseOrderId = 70L, InvoiceNumber = "SI-1", TolerancePercent = (decimal?)2m, PriceVarianceAccountCode = "5100" });
        match.Lines.Should().Equal(new InvoiceMatchLine(200, 40m, 0.55m));
        OrdersApiMapping.InvoiceMatch(3, 70, new InvoiceMatchApi("PO-1", "SI-1", Day), toMatch).Value.Lines.Should().Equal(new InvoiceMatchLine(200, 40m, 0.5m));
        OrdersApiMapping.InvoiceMatch(3, 70, new InvoiceMatchApi("PO-1", " ", Day), toMatch).Error.Code.Should().Be("Api.InvoiceNumber");
    }

    [Fact]
    public void Stock_movements_resolve_item_and_warehouse_codes()
    {
        ReceiveStockRequest receipt = OrdersApiMapping.StockReceipt(3, new StockReceiptApi("widget", "main", Day, 5m, 2m, "3000", "Opening"), Items, Warehouses).Value;
        receipt.Should().BeEquivalentTo(new { CompanyId = 3L, ItemId = 10L, WarehouseId = 1L, Quantity = 5m, UnitCost = 2m, CreditAccountCode = "3000", Reference = "Opening" });
        OrdersApiMapping.StockReceipt(3, new StockReceiptApi("WIDGET", "MAIN", Day, 5m, 2m, " "), Items, Warehouses).Error.Code.Should().Be("Api.CreditAccount");
        OrdersApiMapping.StockReceipt(3, new StockReceiptApi("WIDGET", "BACK", Day, 5m, 2m, "3000"), Items, Warehouses).Error.Code.Should().Be("Api.Warehouse");

        IssueStockRequest issue = OrdersApiMapping.StockIssue(3, new StockIssueApi("BOLT", "SHOP", Day, 2m, "Workshop", "6100", "ADM"), Items, Warehouses).Value;
        issue.Should().BeEquivalentTo(new { ItemId = 11L, WarehouseId = 2L, Quantity = 2m, Reference = "Workshop", ChargeAccountCode = "6100", CostCentre = "ADM", AllowNegative = false });
        OrdersApiMapping.StockIssue(3, new StockIssueApi("BOLT", "SHOP", Day, -1m), Items, Warehouses).Error.Code.Should().Be("Api.Quantity");

        OrdersApiMapping.StockTransfer(3, new StockTransferApi("BOLT", "MAIN", "SHOP", Day, 4m, "Restock"), Items, Warehouses).Value
            .Should().Be(new StockTransferCommand(3, 11, 1, 2, Day, 4m, "Restock"));
        OrdersApiMapping.StockTransfer(3, new StockTransferApi("BOLT", "MAIN", "main", Day, 4m), Items, Warehouses).Error.Code.Should().Be("Api.Warehouse");

        OrdersApiMapping.StockAdjustment(3, new StockAdjustmentApi("WIDGET", "MAIN", Day, 7m, "5300", "ST-1", "damaged"), Items, Warehouses).Value
            .Should().Be(new StockAdjustmentCommand(3, 10, 1, Day, 7m, "5300", "ST-1", "DAMAGED"));
        OrdersApiMapping.StockAdjustment(3, new StockAdjustmentApi("WIDGET", "MAIN", Day, -1m, "5300"), Items, Warehouses).Error.Code.Should().Be("Api.Quantity");
        OrdersApiMapping.StockAdjustment(3, new StockAdjustmentApi("NOPE", "MAIN", Day, 1m, "5300"), Items, Warehouses).Error.Code.Should().Be("Api.Item");
    }
}
