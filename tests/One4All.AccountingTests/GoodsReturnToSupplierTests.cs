using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class GoodsReturnToSupplierTests
{
    private readonly AccountingFixture _fixture;

    public GoodsReturnToSupplierTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Returning_goods_reduces_stock_and_payable_with_price_variance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "RET-SUP", "Returns Vendor", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "RET-ITEM", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "RET-WH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            supplierId = supplier.Id; itemId = item.Id; warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // Receive 100 @ 10 → average cost 10, stock value 1,000.
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 10m, CreditAccountCode = "1100" });

            // Supplier invoice for 1,000 (Cr AP control).
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "RET-PI1", DocumentDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, NetAmount = 1_000m, ExpenseAccountCode = "5000" })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService proc = scope.ServiceProvider.GetRequiredService<IProcurementService>();

            // Guard rails.
            (await proc.ReturnToSupplierAsync(companyId, supplierId, itemId, warehouseId, 0m, 12m, new DateOnly(2026, 3, 10), "RET-0", "5400")).IsFailure.Should().BeTrue();
            (await proc.ReturnToSupplierAsync(companyId, supplierId, itemId, warehouseId, 999m, 12m, new DateOnly(2026, 3, 10), "RET-X", "5400")).IsFailure.Should().BeTrue(); // more than on hand

            // Return 20 @ 12 (agreed PO price). Stock cost 200, credit 240 → 40 favourable price variance.
            var ret = await proc.ReturnToSupplierAsync(companyId, supplierId, itemId, warehouseId, 20m, 12m, new DateOnly(2026, 3, 10), "RET-1", "5400");
            ret.IsSuccess.Should().BeTrue(ret.IsFailure ? ret.Error.ToString() : null);
            ret.Value.StockValue.Should().Be(200m);
            ret.Value.CreditValue.Should().Be(240m);
            ret.Value.PriceVariance.Should().Be(40m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            StockBalance balance = await db.Set<StockBalance>().FirstAsync(b => b.ItemId == itemId && b.WarehouseId == warehouseId);
            balance.QuantityOnHand.Should().Be(80m);      // 100 − 20 returned
            balance.TotalValue.Should().Be(800m);         // at average cost 10

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "5400" && r.Credit == 40m);  // favourable variance

            // Supplier payable nets to 1,000 invoice − 240 credit note = 760.
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 3, 31));
            creditors.Rows.Should().Contain(r => r.TraderCode == "RET-SUP" && r.Total == 760m);
        }
    }
}
