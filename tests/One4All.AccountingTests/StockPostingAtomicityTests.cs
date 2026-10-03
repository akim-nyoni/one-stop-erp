using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers stock postings refused by a closed inventory period: an issue and a stocktake shortage are refused without
/// touching the stock balance, so a later save in the same scope persists nothing and check data stays clean; once
/// the period is reopened the issue goes through.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StockPostingAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public StockPostingAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_refused_stock_posting_leaves_the_balance_untouched()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
            Item item = new(companyId, "SPA-ITEM", "Gadget") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "SPAWH", "Main");
            db.AddRange(item, wh);
            await db.SaveChangesAsync();
            (itemId, warehouseId) = (item.Id, wh.Id);

            (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 4m, CreditAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 4, "INV", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        }

        DateOnly april = new(2026, 4, 10);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = april, Quantity = 3m })).IsFailure.Should().BeTrue();
            (await inventory.AdjustAsync(companyId, itemId, warehouseId, april, 7m, "6800")).IsFailure.Should().BeTrue();

            // Whatever else this scope saves, the refused postings leave no trace.
            await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            StockBalance balance = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == itemId && b.WarehouseId == warehouseId);
            balance.QuantityOnHand.Should().Be(10m);
            balance.TotalValue.Should().Be(40m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();

            // Reopened, the issue goes through.
            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 4, "INV", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            Result<StockMovementResult> issued = await scope.ServiceProvider.GetRequiredService<IInventoryService>()
                .IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = april, Quantity = 3m });
            issued.IsSuccess.Should().BeTrue(issued.IsFailure ? issued.Error.ToString() : null);
            issued.Value.Value.Should().Be(12m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
        }
    }
}
