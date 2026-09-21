using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantBillReversalTests
{
    private const long Waiter = 22501;

    private readonly AccountingFixture _fixture;

    public RestaurantBillReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reversing_a_bill_unwinds_the_revenue_and_returns_the_stock()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long steakItemId;
        long menuId;
        long orderId;
        long billId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item steak = new(companyId, "BR-STEAK", "Steak meat") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(steak);
            Warehouse wh = new(companyId, "BRKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            steakItemId = steak.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = steakItemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 18), Quantity = 50m, UnitCost = 12.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "BR-STEAKPLATE", "Steak Plate", 90m, "4000", "5000", [new RecipeIngredient(steakItemId, 1m)])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "BR1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "BR-ORD-1", Waiter, new DateOnly(2026, 4, 18))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m, taxAmount: 15m);

            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 18), "1000", "2200");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(105m);
            bill.Value.CostOfSales.Should().Be(12m);

            billId = (await db.Set<RestaurantBill>().FirstAsync(b => b.TableOrderId == orderId)).Id;
        }

        // After billing: cash 105, revenue 90, VAT 15, COGS 12, steak stock 49.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "BR-STEAK" && l.Quantity == 49m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "  ")).IsFailure.Should().BeTrue(); // reason required
            (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "Rung on wrong table")).IsSuccess.Should().BeTrue();
            // Cannot reverse twice.
            (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "again")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            RestaurantBill reversed = await db.Set<RestaurantBill>().FirstAsync(b => b.Id == billId);
            reversed.IsReversed.Should().BeTrue();
            reversed.ReversalJournalNumber.Should().NotBeNullOrEmpty();

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "BR-STEAK" && l.Quantity == 50m); // stock returned

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // Everything nets to zero: revenue, VAT, cash, COGS.
            tb.Rows.Should().NotContain(r => r.AccountCode == "4000" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "2200" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "1000" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "5000" && (r.Debit != 0m || r.Credit != 0m));
        }
    }
}
