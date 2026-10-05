using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the standard document layouts (brief §14 S, B 50, C 67, E 94, H 139): a quotation, an order confirmation, delivery and packing
/// notes, a requisition, a goods received note and shelf labels, each built from its records.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DocumentLayoutTests
{
    private readonly AccountingFixture _fixture;

    public DocumentLayoutTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Business_documents_render_from_their_records()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DL-C", "Builder Ltd", Scenario.Usd);
        Supplier supplier = new(companyId, "DL-S", "Brick Co", Scenario.Usd);
        Item brick = new(companyId, "DL-BRK", "Face brick") { CostOfSalesAccountCode = "5000", UnitWeightKg = 2.5m, Barcode = "6001234" };
        Warehouse yard = new(companyId, "DL-WH", "Yard");
        db.AddRange(customer, supplier, brick, yard);
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long po = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "DL-PO1", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(brick.Id, "Face brick", 1_000m, 0.5m)],
        })).Value.PurchaseOrderId;
        long poLine = db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po).Select(l => l.Id).Single();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = po, WarehouseId = yard.Id, GrnNumber = "DL-GRN1", Date = new DateOnly(2026, 3, 2), Lines = [new ReceiveLine(poLine, 1_000m)] }))
            .IsSuccess.Should().BeTrue();
        PurchaseRequisition requisition = new(companyId, supplier.Id, "DL-RQ1", new DateOnly(2026, 3, 3), Scenario.Usd);
        requisition.AddLine(brick.Id, "Face brick", 500m, 0.55m).ExpenseAccountCode = "6000";
        PriceList retail = new(companyId, "RETAIL", "Retail", Scenario.Usd);
        retail.SetPrice(brick.Id, 1m, 0.95m);
        db.AddRange(requisition, retail);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<IQuotationService>().CreateAsync(companyId, customer.Id, "DL-Q1", new DateOnly(2026, 3, 4), new DateOnly(2026, 4, 3), Scenario.Usd,
            [new QuotationLineRequest(brick.Id, "Face brick", 400m, 0.9m, null, "4000")])).IsSuccess.Should().BeTrue();
        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long so = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "DL-SO1", OrderDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, RequestedDeliveryDate = new DateOnly(2026, 3, 9),
            Lines = [new SalesOrderLineRequest(brick.Id, "Face brick", 400m, 0.9m, null, "4000")],
        })).Value.SalesOrderId;
        long soLine = db.Set<One4All.Core.SalesOrders.SalesOrderLine>().Where(l => l.SalesOrderId == so).Select(l => l.Id).Single();
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = so, WarehouseId = yard.Id, Date = new DateOnly(2026, 3, 8), Reference = "DL-D1", Lines = [new DespatchLine(soLine, 400m)], Vehicle = "TRK1", Driver = "Sam",
        })).IsSuccess.Should().BeTrue();

        IDocumentLayoutService documents = scope.ServiceProvider.GetRequiredService<IDocumentLayoutService>();
        string quote = (await documents.QuotationAsync(companyId, "DL-Q1")).Value;
        quote.Should().Contain("QUOTATION").And.Contain("To: Builder Ltd (DL-C)").And.Contain("Valid until: 2026-04-03").And.Contain("DL-BRK Face brick").And.Contain("360.00").And.Contain("Accepted by:");
        (await documents.OrderConfirmationAsync(companyId, "DL-SO1")).Value.Should().Contain("ORDER CONFIRMATION").And.Contain("Delivery wanted: 2026-03-09").And.Contain("360.00");
        string delivery = (await documents.DeliveryNoteAsync(companyId, "DL-D1")).Value;
        delivery.Should().Contain("DELIVERY NOTE").And.Contain("Deliver to: Builder Ltd").And.Contain("Vehicle: TRK1").And.Contain("DL-BRK Face brick").And.NotContain("0.90").And.Contain("Received by:");
        (await documents.DeliveryNoteAsync(companyId, "DL-D1", packing: true)).Value.Should().Contain("PACKING NOTE").And.Contain("Total weight: 1000 kg");
        (await documents.RequisitionAsync(companyId, "DL-RQ1")).Value.Should().Contain("PURCHASE REQUISITION").And.Contain("Face brick [6000]").And.Contain("275.00").And.Contain("Budget checked by:");
        (await documents.GoodsReceivedNoteAsync(companyId, "DL-GRN1")).Value.Should().Contain("GOODS RECEIVED NOTE").And.Contain("Purchase order: DL-PO1").And.Contain("500.00");
        (await documents.ShelfLabelsAsync(companyId, "RETAIL")).Value.Should().Contain("Face brick").And.Contain("USD 0.95").And.Contain("*6001234*");
        (await documents.QuotationAsync(companyId, "NOPE")).Error.Code.Should().Be("Doc.QuotationNotFound");
    }
}
