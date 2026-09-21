using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class InventoryTransferTests
{
    private readonly AccountingFixture _fixture;

    public InventoryTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transfer_moves_stock_between_warehouses_at_average_cost_with_no_gl_impact()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long itemId;
        long storeId;
        long kitchenId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item item = new(companyId, "TR-FLOUR", "Flour") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse store = new(companyId, "TRSTORE", "Main Store");
            Warehouse kitchen = new(companyId, "TRKIT", "Kitchen");
            db.Warehouses.AddRange(store, kitchen);
            await db.SaveChangesAsync();
            itemId = item.Id;
            storeId = store.Id;
            kitchenId = kitchen.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = storeId, Date = new DateOnly(2026, 4, 22), Quantity = 100m, UnitCost = 3.00m, CreditAccountCode = "1100" });
        }

        decimal glBefore;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            glBefore = tb.Rows.First(r => r.AccountCode == "1400").Debit; // stock control (Inventory)
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            // Same-warehouse and over-quantity transfers are refused.
            (await inv.TransferAsync(companyId, itemId, storeId, storeId, new DateOnly(2026, 4, 22), 10m)).IsFailure.Should().BeTrue();
            (await inv.TransferAsync(companyId, itemId, storeId, kitchenId, new DateOnly(2026, 4, 22), 999m)).IsFailure.Should().BeTrue();

            // Move 30 flour to the kitchen (value 90 at avg 3).
            var transfer = await inv.TransferAsync(companyId, itemId, storeId, kitchenId, new DateOnly(2026, 4, 22), 30m, "XFER-1");
            transfer.IsSuccess.Should().BeTrue(transfer.IsFailure ? transfer.Error.ToString() : null);
            transfer.Value.Should().Be(90m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "TR-FLOUR" && l.WarehouseCode == "TRSTORE" && l.Quantity == 70m && l.Value == 210m);
            valuation.Should().Contain(l => l.ItemCode == "TR-FLOUR" && l.WarehouseCode == "TRKIT" && l.Quantity == 30m && l.Value == 90m);

            // Stock control account is unchanged (GL-neutral transfer) and the TB still balances.
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.First(r => r.AccountCode == "1400").Debit.Should().Be(glBefore);
        }
    }
}
