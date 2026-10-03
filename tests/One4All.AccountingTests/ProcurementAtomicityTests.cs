using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers procurement steps as single units of work: a goods receipt whose second line is unknown receives nothing; an
/// invoice match whose second line is outside price tolerance marks no line invoiced; a supplier return refused by a
/// closed period leaves stock untouched — each even after a save in the same scope, with check data clean.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ProcurementAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public ProcurementAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receipts_matches_and_returns_stand_or_fall_together()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long bolt;
        long nut;
        long warehouseId;
        long yearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
            Supplier supplier = new(companyId, "PA-SUP", "Fixings Supplier", Scenario.Usd);
            Item b = new(companyId, "PA-BOLT", "Bolt") { CostOfSalesAccountCode = "5000" };
            Item n = new(companyId, "PA-NUT", "Nut") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "PAWH", "Main");
            db.AddRange(supplier, b, n, wh);
            await db.SaveChangesAsync();
            (supplierId, bolt, nut, warehouseId) = (supplier.Id, b.Id, n.Id, wh.Id);
        }

        long poId;
        List<long> poLines;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "PA-PO", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(bolt, "Bolt", 10m, 2m), new PurchaseOrderLineRequest(nut, "Nut", 10m, 1m)],
            })).Value.PurchaseOrderId;
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            poLines = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();

            // A receipt whose second line does not exist receives nothing at all.
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = warehouseId, GrnNumber = "PA-GRN-X", Date = new DateOnly(2026, 3, 5),
                Lines = [new ReceiveLine(poLines[0], 10m), new ReceiveLine(long.MaxValue, 1m)],
            })).Error.Code.Should().Be("Pop.LineNotFound");
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.StockBalances.AsNoTracking().Where(s => s.ItemId == bolt).SumAsync(s => s.QuantityOnHand)).Should().Be(0m);
            (await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == poId).SumAsync(l => l.ReceivedQuantity)).Should().Be(0m);
            (await db.Set<GoodsReceivedNote>().CountAsync(g => g.CompanyId == companyId)).Should().Be(0);

            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = warehouseId, GrnNumber = "PA-GRN-1", Date = new DateOnly(2026, 3, 5),
                Lines = [new ReceiveLine(poLines[0], 10m), new ReceiveLine(poLines[1], 10m)],
            })).IsSuccess.Should().BeTrue();

            // A match whose second line is outside price tolerance marks neither line invoiced.
            (await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "PA-INV", Date = new DateOnly(2026, 3, 8), TolerancePercent = 5m,
                Lines = [new InvoiceMatchLine(poLines[0], 10m, 2m), new InvoiceMatchLine(poLines[1], 10m, 1.50m)],
            })).Error.Code.Should().Be("Pop.PriceMismatch");
            await db.SaveChangesAsync();
            (await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == poId).SumAsync(l => l.InvoicedQuantity)).Should().Be(0m);

            (await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "PA-INV", Date = new DateOnly(2026, 3, 8), TolerancePercent = 5m,
                Lines = [new InvoiceMatchLine(poLines[0], 10m, 2m), new InvoiceMatchLine(poLines[1], 10m, 1m)],
            })).IsSuccess.Should().BeTrue();

            // A return refused by a closed period leaves the stock as it was.
            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 4, "POP", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
            (await pop.ReturnToSupplierAsync(companyId, supplierId, bolt, warehouseId, 4m, 2m, new DateOnly(2026, 4, 2), "PA-RTN", "6800")).IsFailure.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.StockBalances.AsNoTracking().SingleAsync(s => s.ItemId == bolt)).QuantityOnHand.Should().Be(10m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
        }
    }
}
