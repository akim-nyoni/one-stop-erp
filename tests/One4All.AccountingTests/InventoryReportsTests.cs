using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the INV-numbered inventory reports (brief §14 E): two receipts at a rising cost and an issue; the register agrees
/// with the stock control account, the item is overstocked against its reorder ceiling, the cost change is listed; every
/// INV report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class InventoryReportsTests
{
    private readonly AccountingFixture _fixture;

    public InventoryReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Inventory_reports_reflect_the_stock_register()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Item bolt = new(companyId, "IR-BOLT", "Bolt") { CostOfSalesAccountCode = "5000", ReorderLevel = 5m, ReorderQuantity = 5m };
        Warehouse store = new(companyId, "IRMAIN", "Main store");
        db.AddRange(bolt, store);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        async Task Receive(DateOnly date, decimal quantity, decimal cost, string reference) => (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = bolt.Id, WarehouseId = store.Id, Date = date, Quantity = quantity, UnitCost = cost, CreditAccountCode = "1100", Reference = reference,
        })).IsSuccess.Should().BeTrue();
        await Receive(new DateOnly(2026, 2, 1), 20m, 10m, "IR-R1");
        await Receive(new DateOnly(2026, 3, 1), 10m, 12m, "IR-R2");
        (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = bolt.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 10), Quantity = 5m, Reference = "IR-I1" }))
            .IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("INV78")).Should().Contain("Warehouse IRMAIN,1,25,").And.Contain("Difference (register − ledger): 0.00");
        (await Render("INV79")).Should().Contain("IR-BOLT Bolt,IRMAIN,25,0,0,25,25,");
        (await Render("INV86")).Should().Contain("IR-BOLT Bolt,25,10,15,");
        (await Render("INV87")).Should().NotContain("IR-BOLT");
        (await Render("INV96")).Should().Contain("IR-BOLT Bolt,2026-03-01,IR-R2,10.00,12.00,+20.0%,10");

        foreach (string code in InventoryReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
