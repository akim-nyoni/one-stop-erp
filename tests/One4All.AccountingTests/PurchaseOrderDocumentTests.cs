using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the purchase order document built from the order, supplier and items; cancelled orders are refused.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PurchaseOrderDocumentTests
{
    private readonly AccountingFixture _fixture;

    public PurchaseOrderDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Purchase_order_renders_with_supplier_items_and_total()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long boltId;
        long nutId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "POD-S", "Parts Supplier Ltd", Scenario.Usd) { PaymentTermsDays = 45 };
            db.Suppliers.Add(supplier);
            Item bolt = new(companyId, "POD-BOLT", "M8 bolt") { CostOfSalesAccountCode = "5000" };
            Item nut = new(companyId, "POD-NUT", "M8 nut") { CostOfSalesAccountCode = "5000" };
            db.Items.AddRange(bolt, nut);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
            boltId = bolt.Id;
            nutId = nut.Id;
        }

        long orderId;
        long cancelledId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            orderId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "POD-1", OrderDate = new DateOnly(2026, 7, 2), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(boltId, "M8 bolt", 100m, 1.25m), new PurchaseOrderLineRequest(nutId, "M8 nut", 100m, 0.40m)],
            })).Value.PurchaseOrderId;
            cancelledId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "POD-2", OrderDate = new DateOnly(2026, 7, 2), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(boltId, "M8 bolt", 1m, 1.25m)],
            })).Value.PurchaseOrderId;
            (await pop.CancelPurchaseOrderAsync(companyId, cancelledId, "test")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseOrderDocumentService documents = scope.ServiceProvider.GetRequiredService<IPurchaseOrderDocumentService>();

            var built = await documents.BuildAsync(companyId, orderId);
            built.IsSuccess.Should().BeTrue(built.IsFailure ? built.Error.ToString() : null);
            PurchaseOrderDocument doc = built.Value;
            doc.SupplierCode.Should().Be("POD-S");
            doc.OrderNumber.Should().Be("POD-1");
            doc.PaymentTermsDays.Should().Be(45);
            doc.Status.Should().Be("Open");
            doc.Lines.Select(l => l.ItemCode).Should().Equal("POD-BOLT", "POD-NUT");
            doc.Total.Should().Be(165.00m);

            string text = (await documents.RenderTextAsync(companyId, orderId)).Value;
            text.Should().Contain("PURCHASE ORDER").And.Contain("Parts Supplier Ltd (POD-S)").And.Contain("POD-BOLT M8 bolt").And.Contain("165.00").And.Contain("45 days");

            (await documents.BuildAsync(companyId, cancelledId)).Error.Code.Should().Be("Pop.Cancelled");
            (await documents.BuildAsync(companyId, 999_999)).Error.Code.Should().Be("Pop.OrderNotFound");
        }
    }
}
