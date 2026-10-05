using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers two-step transfers and consignment stock (brief §14 E 91, 101): dispatch into a transit location, a short receipt
/// written off under the TRANSIT reason, the guards on each step, goods in transit at a date, and stock held at a customer's site.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StockTransferTests
{
    private readonly AccountingFixture _fixture;

    public StockTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transfers_travel_through_transit_and_consignment_stock_is_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer partner = new(companyId, "TCONS", "Retail Partner", Scenario.Usd);
        db.Customers.Add(partner);
        await db.SaveChangesAsync();
        Item cement = new(companyId, "TCEM", "Cement 50kg") { CostOfSalesAccountCode = "5000" };
        Warehouse main = new(companyId, "TMAIN", "Main"), branch = new(companyId, "TBR", "Branch"), road = new(companyId, "TROAD", "On the road") { IsTransit = true };
        Warehouse site = new(companyId, "TSITE", "Partner site") { ConsignmentCustomerId = partner.Id };
        db.AddRange(cement, main, branch, road, site, new StockReasonCode(companyId, "TRANSIT", "Lost in transit"));
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = cement.Id, WarehouseId = main.Id, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 2m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IStockTransferService transfers = scope.ServiceProvider.GetRequiredService<IStockTransferService>();
        DispatchTransferRequest Dispatch(decimal quantity, DateOnly date, long transit) => new()
        {
            CompanyId = companyId, ItemId = cement.Id, FromWarehouseId = main.Id, ToWarehouseId = branch.Id, TransitWarehouseId = transit, Date = date, Quantity = quantity,
        };
        (await transfers.DispatchAsync(Dispatch(6m, new DateOnly(2026, 3, 10), branch.Id))).Error.Code.Should().Be("Inv.NotTransit");
        DispatchTransferResult first = (await transfers.DispatchAsync(Dispatch(6m, new DateOnly(2026, 3, 10), road.Id))).Value;
        first.Value.Should().Be(12m);
        DispatchTransferResult second = (await transfers.DispatchAsync(Dispatch(2m, new DateOnly(2026, 3, 12), road.Id))).Value;

        (await transfers.ReceiveAsync(companyId, first.TransferId, new DateOnly(2026, 3, 15), 7m, "7500")).Error.Code.Should().Be("Inv.ReceivedQuantity");
        (await transfers.ReceiveAsync(companyId, first.TransferId, new DateOnly(2026, 3, 15), 5m)).Error.Code.Should().Be("Inv.LossAccountRequired");
        (await transfers.ReceiveAsync(companyId, first.TransferId, new DateOnly(2026, 3, 15), 5m, "7500")).Value.Should().Be(2m);
        (await transfers.ReceiveAsync(companyId, first.TransferId, new DateOnly(2026, 3, 16), 5m, "7500")).Error.Code.Should().Be("Inv.TransferReceived");
        (await db.StockTransfers.AsNoTracking().SingleAsync(t => t.Id == first.TransferId)).Status.Should().Be(StockTransferStatus.ReceivedShort);

        async Task<decimal> OnHand(Warehouse w) => await db.StockBalances.AsNoTracking().Where(b => b.ItemId == cement.Id && b.WarehouseId == w.Id).Select(b => b.QuantityOnHand).SingleAsync();
        (await OnHand(main)).Should().Be(2m);
        (await OnHand(road)).Should().Be(2m);
        (await OnHand(branch)).Should().Be(5m);

        (await inventory.TransferAsync(companyId, cement.Id, main.Id, site.Id, new DateOnly(2026, 3, 20), 1m, "CONS-1")).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly asAt)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = asAt };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("INV91", new DateOnly(2026, 3, 13))).Should().Contain($"{first.TransferNumber},TCEM Cement 50kg,TMAIN,TBR,2026-03-10,3,6,12")
            .And.Contain($"{second.TransferNumber},TCEM Cement 50kg,TMAIN,TBR,2026-03-12,1,2,4");
        string later = await Render("INV91", new DateOnly(2026, 3, 31));
        later.Should().Contain($"{second.TransferNumber},").And.NotContain(first.TransferNumber);
        (await Render("INV82", new DateOnly(2026, 3, 31))).Should().Contain("TRANSIT,Lost in transit,1,0,1,-2");
        (await Render("INV101", new DateOnly(2026, 3, 31))).Should().Contain("Retail Partner,TSITE,TCEM Cement 50kg,1,2026-03-20,2");
    }
}
