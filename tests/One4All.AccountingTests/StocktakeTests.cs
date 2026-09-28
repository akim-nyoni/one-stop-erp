using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end stocktake: freeze a warehouse's on-hand, capture counts, post the variances at moving-average
/// cost (a shortage and a surplus), and confirm the stock and the ledger agree and the books balance.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StocktakeTests
{
    private readonly AccountingFixture _fixture;

    public StocktakeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_stocktake_posts_variances_and_reconciles_stock()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long sodaId;
        long bunId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item soda = new(companyId, "SODA", "Soda 500ml") { CostOfSalesAccountCode = "5000" };
            Item bun = new(companyId, "BUN", "Bread Bun") { CostOfSalesAccountCode = "5000" };
            db.Items.Add(soda);
            db.Items.Add(bun);
            Warehouse wh = new(companyId, "WH1", "Shop Floor");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            sodaId = soda.Id;
            bunId = bun.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = sodaId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = bunId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 50m, UnitCost = 2.00m, CreditAccountCode = "1100" });
        }

        long stocktakeId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStocktakeService stocktake = scope.ServiceProvider.GetRequiredService<IStocktakeService>();
            stocktakeId = (await stocktake.StartAsync(companyId, warehouseId, new DateOnly(2026, 3, 31), "ST-1")).Value;

            // Soda short by 5 (95 counted), buns over by 2 (52 counted).
            (await stocktake.RecordCountAsync(companyId, stocktakeId, sodaId, 95m)).IsSuccess.Should().BeTrue();
            (await stocktake.RecordCountAsync(companyId, stocktakeId, bunId, 52m)).IsSuccess.Should().BeTrue();

            var post = await stocktake.PostAsync(companyId, stocktakeId, new DateOnly(2026, 3, 31), "5300");
            post.IsSuccess.Should().BeTrue(post.IsFailure ? post.Error.ToString() : null);

            // Shortage 5 @ 1.00 = −5.00; surplus 2 @ 2.00 = +4.00 → net −1.00 over two adjustments.
            post.Value.AdjustmentsPosted.Should().Be(2);
            post.Value.TotalVarianceValue.Should().Be(-1.00m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "SODA" && l.Quantity == 95m && l.Value == 95m);
            valuation.Should().Contain(l => l.ItemCode == "BUN" && l.Quantity == 52m && l.Value == 104m);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            decimal debit = await db.Journals.Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).SumAsync(l => l.BaseDebit);
            decimal credit = await db.Journals.Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).SumAsync(l => l.BaseCredit);
            debit.Should().Be(credit);
        }

        // Posting a second time is refused (the session is no longer a draft).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStocktakeService stocktake = scope.ServiceProvider.GetRequiredService<IStocktakeService>();
            (await stocktake.PostAsync(companyId, stocktakeId, new DateOnly(2026, 3, 31), "5300")).IsFailure.Should().BeTrue();
        }
    }
}
