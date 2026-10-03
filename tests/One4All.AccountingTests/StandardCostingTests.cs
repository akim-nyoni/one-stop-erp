using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers standard costing: with a standard of 10, receipts of 10 at 12 and 5 at 9 go into stock at 150, the 15 of
/// difference to price variance; an issue of 6 costs 60; moving the standard to 11 revalues the 9 on hand by 9; the next
/// issue costs 11. A standard item without a standard cannot be received.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandardCostingTests
{
    private readonly AccountingFixture _fixture;

    public StandardCostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Standard_items_are_valued_at_standard_with_variances_aside()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "5190", "Purchase Price Variance", AccountType.Expense));
        Item gear = new(companyId, "STD-G", "Gear", ItemType.Stock, CostingMethod.Standard) { CostOfSalesAccountCode = "5000", StandardCost = 10m, PriceVarianceAccountCode = "5190" };
        Item unset = new(companyId, "STD-X", "Unpriced", ItemType.Stock, CostingMethod.Standard) { CostOfSalesAccountCode = "5000" };
        Warehouse wh = new(companyId, "STDW", "Main");
        db.AddRange(gear, unset, wh);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await Receive(inventory, companyId, unset.Id, wh.Id, 1m, 5m)).Error.Code.Should().Be("Inv.NoStandardCost");
        (await Receive(inventory, companyId, gear.Id, wh.Id, 10m, 12m)).Value.Value.Should().Be(100m);
        (await Receive(inventory, companyId, gear.Id, wh.Id, 5m, 9m)).Value.Value.Should().Be(50m);

        (await Issue(inventory, companyId, gear.Id, wh.Id, 6m)).Value.Value.Should().Be(60m);
        (await inventory.RevalueStandardCostAsync(companyId, gear.Id, 11m, new DateOnly(2026, 3, 25), "5300")).Value.Should().Be(9m);
        (await db.Items.AsNoTracking().SingleAsync(i => i.Id == gear.Id)).StandardCost.Should().Be(11m);
        (await Issue(inventory, companyId, gear.Id, wh.Id, 1m)).Value.Value.Should().Be(11m);

        StockBalance balance = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == gear.Id);
        (balance.QuantityOnHand, balance.TotalValue, balance.AverageCost).Should().Be((8m, 88m, 11m));

        (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1400").Debit.Should().Be(88m);
        tb.Rows.Single(r => r.AccountCode == "5190").Debit.Should().Be(15m);
        tb.Rows.Single(r => r.AccountCode == "5000").Debit.Should().Be(71m);
        tb.Rows.Single(r => r.AccountCode == "5300").Credit.Should().Be(9m);
    }

    private static Task<One4All.Core.Common.Result<StockMovementResult>> Receive(IInventoryService inventory, long companyId, long itemId, long warehouseId, decimal quantity, decimal unitCost)
        => inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Quantity = quantity, UnitCost = unitCost, CreditAccountCode = "1100",
        });

    private static Task<One4All.Core.Common.Result<StockMovementResult>> Issue(IInventoryService inventory, long companyId, long itemId, long warehouseId, decimal quantity)
        => inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 25), Quantity = quantity });
}
