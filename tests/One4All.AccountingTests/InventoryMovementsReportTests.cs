using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class InventoryMovementsReportTests
{
    private readonly AccountingFixture _fixture;

    public InventoryMovementsReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stock_movements_report_lists_receipts_issues_transfers_and_adjustments()
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
            Item item = new(companyId, "MV-RICE", "Rice") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse store = new(companyId, "MVSTORE", "Store");
            Warehouse kitchen = new(companyId, "MVKIT", "Kitchen");
            db.Warehouses.AddRange(store, kitchen);
            await db.SaveChangesAsync();
            itemId = item.Id;
            storeId = store.Id;
            kitchenId = kitchen.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = storeId, Date = new DateOnly(2026, 4, 25), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100", Reference = "GRN-9" });
            await inv.TransferAsync(companyId, itemId, storeId, kitchenId, new DateOnly(2026, 4, 25), 40m, "XFER-9");
            await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = kitchenId, Date = new DateOnly(2026, 4, 25), Quantity = 10m, Reference = "COOK-9" });
            await inv.AdjustAsync(companyId, itemId, storeId, new DateOnly(2026, 4, 25), 58m, "5300", "STK-9"); // store 60 → 58, shortage 2
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string moves = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "INV-MOVE", ps, ReportFormat.Html)).Value.Content);
            moves.Should().Contain("MV-RICE");
            moves.Should().Contain("Receipt").And.Contain("Transfer").And.Contain("Issue").And.Contain("Adjustment");
            moves.Should().Contain("GRN-9").And.Contain("XFER-9").And.Contain("COOK-9").And.Contain("STK-9");
        }
    }
}
