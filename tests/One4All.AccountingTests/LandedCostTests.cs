using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers landed costs end to end: 32 of freight on a receipt of bolts (100) and nuts (60) is shared 20 / 12 by value;
/// half the bolts have already been issued, so 10 of their share goes to cost of sales and 10 onto the bolts still on
/// hand (average 10 → 12); the nuts take all 12 (average 2 → 2.40); the same charges cannot be applied twice and check
/// data stays clean.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LandedCostAccountingTests
{
    private readonly AccountingFixture _fixture;

    public LandedCostAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Freight_is_capitalised_on_stock_on_hand_and_expensed_on_stock_issued()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2485", "Landed Cost Clearing", AccountType.Liability));
        Supplier supplier = new(companyId, "LC-SUP", "Overseas Supplier", Scenario.Usd);
        Item bolt = new(companyId, "LC-BOLT", "Bolt") { CostOfSalesAccountCode = "5000" };
        Item nut = new(companyId, "LC-NUT", "Nut") { CostOfSalesAccountCode = "5000" };
        Warehouse wh = new(companyId, "LCWH", "Main");
        db.AddRange(supplier, bolt, nut, wh);
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "LC-PO", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(bolt.Id, "Bolt", 10m, 10m), new PurchaseOrderLineRequest(nut.Id, "Nut", 30m, 2m)],
        })).Value.PurchaseOrderId;
        List<long> poLines = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();
        long grnId = (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = wh.Id, GrnNumber = "LC-GRN", Date = new DateOnly(2026, 3, 5),
            Lines = [new ReceiveLine(poLines[0], 10m), new ReceiveLine(poLines[1], 30m)],
        })).Value.GrnId;

        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = bolt.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 8), Quantity = 5m })).IsSuccess.Should().BeTrue();

        ILandedCostService landed = scope.ServiceProvider.GetRequiredService<ILandedCostService>();
        LandedCostRequest freight = new()
        {
            CompanyId = companyId, GoodsReceivedNoteId = grnId, Date = new DateOnly(2026, 3, 10), Amount = 32m, Basis = LandedCostBasis.Value,
            ClearingAccountCode = "2485", Reference = "FWD-1001",
        };
        var result = await landed.ApplyAsync(freight);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
        result.Value.Lines.Should().BeEquivalentTo(
        [
            new { ItemCode = "LC-BOLT", Apportioned = 20m, Capitalised = 10m, ToCostOfSales = 10m },
            new { ItemCode = "LC-NUT", Apportioned = 12m, Capitalised = 12m, ToCostOfSales = 0m },
        ]);
        (await landed.ApplyAsync(freight)).Error.Code.Should().Be("Landed.Duplicate");

        StockBalance bolts = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == bolt.Id);
        bolts.Should().BeEquivalentTo(new { QuantityOnHand = 5m, TotalValue = 60m, AverageCost = 12m });
        StockBalance nuts = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == nut.Id);
        nuts.Should().BeEquivalentTo(new { QuantityOnHand = 30m, TotalValue = 72m, AverageCost = 2.4m });

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1400").Debit.Should().Be(132m); // 160 received − 50 issued + 22 capitalised
        tb.Rows.Single(r => r.AccountCode == "5000").Debit.Should().Be(60m);  // 50 issued + 10 freight on issued bolts
        tb.Rows.Single(r => r.AccountCode == "2485").Credit.Should().Be(32m);
        (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
    }
}
