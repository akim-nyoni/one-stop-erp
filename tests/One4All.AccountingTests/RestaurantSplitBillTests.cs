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
public sealed class RestaurantSplitBillTests
{
    private const long Waiter = 20201;

    private readonly AccountingFixture _fixture;

    public RestaurantSplitBillTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Order_can_be_split_billed_with_a_service_charge()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long pizzaId;
        long tableId;
        long warehouseId;
        long lineId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item dough = new(companyId, "DOUGH", "Dough") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(dough);
            Warehouse wh = new(companyId, "KIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = dough.Id, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 3.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            pizzaId = (await res.CreateMenuItemAsync(companyId, "PIZZA", "Pizza", 40m, "4000", "5000", [new RecipeIngredient(dough.Id, 1m)])).Value;
            tableId = (await res.CreateTableAsync(companyId, "T5", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "ORD-5", Waiter, new DateOnly(2026, 3, 2))).Value;
            await res.AddItemAsync(companyId, orderId, pizzaId, 4m); // 4 pizzas, no tax

            lineId = await db.Set<TableOrderLine>().Where(l => l.TableOrderId == orderId).Select(l => l.Id).FirstAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // First guest pays for 1 pizza (net 40).
            var first = await res.BillPartialAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 2), [new SplitPortion(lineId, 1m)], "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.ToString() : null);
            first.Value.Net.Should().Be(40m);
            first.Value.FullyBilled.Should().BeFalse();

            // Cannot split more than remains (3 left).
            (await res.BillPartialAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 2), [new SplitPortion(lineId, 5m)], "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsFailure.Should().BeTrue();

            // The rest (3 pizzas net 120) plus a 12 service charge → gross 132.
            var rest = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 2), "1000", serviceChargeAmount: 12m, serviceChargeAccountCode: "4600", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            rest.IsSuccess.Should().BeTrue(rest.IsFailure ? rest.Error.ToString() : null);
            rest.Value.Net.Should().Be(120m);
            rest.Value.ServiceCharge.Should().Be(12m);
            rest.Value.Gross.Should().Be(132m);
            rest.Value.FullyBilled.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<RestaurantTable>().FindAsync(tableId))!.Status.Should().Be(TableStatus.Free);

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "DOUGH" && l.Quantity == 96m); // 4 issued

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 160m);  // total pizza revenue (40+120)
            tb.Rows.Should().Contain(r => r.AccountCode == "4600" && r.Credit == 12m);   // service charge income
            tb.Rows.Should().Contain(r => r.AccountCode == "5000" && r.Debit == 12m);    // COGS 4 × 3.00
        }
    }
}
