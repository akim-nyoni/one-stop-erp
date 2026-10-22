using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Restaurant;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a restaurant bill as one unit of work: when the cost-of-sales issue is refused (the inventory period is
/// closed) after the revenue has been posted, nothing remains - no journal, no billed quantity, no stock movement -
/// even after a save in the same scope; once the period reopens the same order bills normally.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RestaurantBillAtomicityTests
{
    private const long Waiter = 23101;

    private readonly AccountingFixture _fixture;

    public RestaurantBillAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_bill_whose_cost_of_sales_fails_leaves_nothing_behind()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        long warehouseId;
        long pattyId;
        long orderId;
        DateOnly may5 = new(2026, 5, 5);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
            Item patty = new(companyId, "RBA-PATTY", "Patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Warehouse wh = new(companyId, "RBAKIT", "Kitchen");
            db.AddRange(patty, wh);
            await db.SaveChangesAsync();
            (pattyId, warehouseId) = (patty.Id, wh.Id);

            await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 10), Quantity = 5m, UnitCost = 8m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            long burger = (await res.CreateMenuItemAsync(companyId, "RBA-BURGER", "Burger", 50m, "4000", "5000", [new RecipeIngredient(pattyId, 1m)])).Value;
            long table = (await res.CreateTableAsync(companyId, "RBA1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, table, "RBA-ORD", Waiter, may5)).Value;
            (await res.AddItemAsync(companyId, orderId, burger, 2m)).IsSuccess.Should().BeTrue();

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 5, "INV", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IRestaurantService>().BillOrderAsync(companyId, orderId, warehouseId, may5, "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsFailure.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableOrder order = await db.TableOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(TableOrderStatus.Open);
            order.Lines.Sum(l => l.BilledQuantity).Should().Be(0m);
            (await db.Journals.CountAsync(j => j.CompanyId == companyId && j.PostingDate == may5)).Should().Be(0);
            (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == pattyId)).QuantityOnHand.Should().Be(5m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 5, "INV", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            var billed = await scope.ServiceProvider.GetRequiredService<IRestaurantService>().BillOrderAsync(companyId, orderId, warehouseId, may5, "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            billed.IsSuccess.Should().BeTrue(billed.IsFailure ? billed.Error.ToString() : null);
            (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == pattyId)).QuantityOnHand.Should().Be(3m);
        }
    }
}
