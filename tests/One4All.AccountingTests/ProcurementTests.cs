using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ProcurementTests
{
    private readonly AccountingFixture _fixture;

    public ProcurementTests(AccountingFixture fixture) => _fixture = fixture;

    private static async Task<(long companyId, long supplierId, long itemId, long warehouseId)> SeedAsync(ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "POPSUP", "Parts Supplier", Scenario.Usd);
        db.Suppliers.Add(supplier);
        Item item = new(companyId, "BOLT", "Bolt") { CostOfSalesAccountCode = "5000" };
        db.Items.Add(item);
        Warehouse wh = new(companyId, "WH1", "Main");
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();
        return (companyId, supplier.Id, item.Id, wh.Id);
    }

    [SkippableFact]
    public async Task Po_grn_three_way_match_accrues_grni_and_clears_to_ap()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        (long companyId, long supplierId, long itemId, long warehouseId) = await SeedAsync(provider);

        long poId;
        long poLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            var po = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "PO-1", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(itemId, "Bolt", 100m, 2.00m)],
            });
            po.IsSuccess.Should().BeTrue(po.IsFailure ? po.Error.ToString() : null);
            poId = po.Value.PurchaseOrderId;

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            poLineId = db.Set<Core.Procurement.PurchaseOrderLine>().First(l => l.PurchaseOrderId == poId).Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            var grn = await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = warehouseId, GrnNumber = "GRN-1", Date = new DateOnly(2026, 3, 5),
                Lines = [new ReceiveLine(poLineId, 100m)],
            });
            grn.IsSuccess.Should().BeTrue(grn.IsFailure ? grn.Error.ToString() : null);
            grn.Value.AccruedValue.Should().Be(200m); // 100 × 2.00 accrued to GRNI
        }

        // A price outside tolerance is rejected.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            var bad = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "SINV-BAD", Date = new DateOnly(2026, 3, 8), TolerancePercent = 5m,
                Lines = [new InvoiceMatchLine(poLineId, 100m, 3.00m)],
            });
            bad.IsFailure.Should().BeTrue();
            bad.Error.Code.Should().Be("Pop.PriceMismatch");
        }

        // A matching invoice clears GRNI to AP.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            var match = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "SINV-1", Date = new DateOnly(2026, 3, 8), TolerancePercent = 5m,
                Lines = [new InvoiceMatchLine(poLineId, 100m, 2.00m)],
            });
            match.IsSuccess.Should().BeTrue(match.IsFailure ? match.Error.ToString() : null);
            match.Value.InvoiceValue.Should().Be(200m);
        }

        // The match rules (thirteenth audit #3): a clerk cannot widen the company's price rule; a supplier's own rule applies; the invoice quantity may exceed the
        // receipt only within the quantity tolerance.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            long po2 = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "PO-2", OrderDate = new DateOnly(2026, 3, 9), Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(itemId, "Bolt", 10m, 2.00m)],
            })).Value.PurchaseOrderId;
            long line2 = db.Set<Core.Procurement.PurchaseOrderLine>().First(l => l.PurchaseOrderId == po2).Id;
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = po2, WarehouseId = warehouseId, GrnNumber = "GRN-2", Date = new DateOnly(2026, 3, 9), Lines = [new ReceiveLine(line2, 10m)] }))
                .IsSuccess.Should().BeTrue();
            MatchInvoiceRequest At(string number, decimal quantity, decimal price, decimal? tolerance = null) => new()
            {
                CompanyId = companyId, PurchaseOrderId = po2, InvoiceNumber = number, Date = new DateOnly(2026, 3, 10), TolerancePercent = tolerance, PriceVarianceAccountCode = "6100",
                Lines = [new InvoiceMatchLine(line2, quantity, price)],
            };
            (await pop.MatchInvoiceAsync(At("SINV-WIDE", 10m, 3.00m, 60m))).Error.Code.Should().Be("Pop.ToleranceAboveRule", "a clerk cannot widen the company's 5% rule");
            (await pop.MatchInvoiceAsync(At("SINV-WIDE", 10m, 3.00m))).Error.Code.Should().Be("Pop.PriceMismatch", "the company's 5% rule applies when none is given");
            (await db.Suppliers.SingleAsync(s => s.Id == supplierId)).InvoicePriceTolerancePercent = 60m;
            (await db.Companies.SingleAsync(c => c.Id == companyId)).InvoiceQuantityTolerancePercent = 2m;
            await db.SaveChangesAsync();
            (await pop.MatchInvoiceAsync(At("SINV-QTY", 10.5m, 2.00m))).Error.Code.Should().Be("Pop.QuantityMismatch", "5% over the receipt is beyond the 2% quantity tolerance");
            var wide = await pop.MatchInvoiceAsync(At("SINV-WIDE", 10.2m, 3.00m));
            wide.IsSuccess.Should().BeTrue(wide.IsFailure ? wide.Error.ToString() : null, "within the supplier's own 60% price rule and the 2% quantity tolerance");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 3, 31));
            creditors.Total.Should().Be(230.6m); // owed to the supplier via AP: 200 matched first, plus SINV-WIDE (10.2 at 3.00) from the tolerance checks

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            // 100 on PO-1 plus 10 on PO-2, both received at 2.00; SINV-WIDE's 1.00 more on the 10 received (all still in stock) goes onto the stock's value.
            valuation.Should().Contain(l => l.ItemCode == "BOLT" && l.Quantity == 110m && l.Value == 230m);
        }
    }
}
