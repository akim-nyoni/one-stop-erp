using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers batch, serial and expiry traceability and adjustment reasons (brief §14 E 82, 88–90): batches received with and without
/// expiry, picked first-expiry-first-out and refused when short; serials one unit each and never received twice; adjustments analysed
/// by an active reason code; INV82, INV88, INV89 and INV90 read them back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LotTrackingTests
{
    private readonly AccountingFixture _fixture;

    public LotTrackingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lots_are_traced_through_movements_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Item milk = new(companyId, "LMILK", "Milk powder") { Tracking = ItemTracking.Batch, ShelfLifeDays = 180, CostOfSalesAccountCode = "5000" };
        Item phone = new(companyId, "LPHONE", "Handset") { Tracking = ItemTracking.Serial, CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "LWH", "Main Store");
        db.AddRange(milk, phone, store, new StockReasonCode(companyId, "DAMAGED", "Damaged in store"));
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        ILotTrackingService lots = scope.ServiceProvider.GetRequiredService<ILotTrackingService>();
        async Task<long> Receive(Item item, decimal quantity, decimal cost)
            => (await inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = quantity, UnitCost = cost, CreditAccountCode = "1100",
            })).Value.MovementId;
        async Task<long> Issue(Item item, decimal quantity)
            => (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 5), Quantity = quantity, Reference = "ISS" }))
                .Value.MovementId;

        long milkIn = await Receive(milk, 10m, 2m);
        (await lots.RecordAsync(companyId, milkIn, [new LotQuantity("B1", 6m)])).Error.Code.Should().Be("Inv.LotQuantityMismatch");
        (await lots.RecordAsync(companyId, milkIn, [new LotQuantity("B1", 6m, new DateOnly(2026, 4, 10)), new LotQuantity("B2", 4m)])).IsSuccess.Should().BeTrue();
        (await lots.RecordAsync(companyId, milkIn, [new LotQuantity("B3", 10m)])).Error.Code.Should().Be("Inv.LotsAlreadyRecorded");

        var picks = await lots.SuggestPicksAsync(companyId, milk.Id, store.Id, 5m, new DateOnly(2026, 3, 5));
        picks.Picks.Should().Equal(new LotQuantity("B1", 5m, new DateOnly(2026, 4, 10)));
        (await lots.RecordAsync(companyId, await Issue(milk, 5m), picks.Picks)).IsSuccess.Should().BeTrue();
        long second = await Issue(milk, 2m);
        (await lots.RecordAsync(companyId, second, [new LotQuantity("B1", 2m)])).Error.Code.Should().Be("Inv.LotShort");
        (await lots.RecordAsync(companyId, second, [new LotQuantity("B2", 2m)])).IsSuccess.Should().BeTrue();

        long phonesIn = await Receive(phone, 2m, 100m);
        (await lots.RecordAsync(companyId, phonesIn, [new LotQuantity("S-1", 1m), new LotQuantity("s-1", 1m)])).Error.Code.Should().Be("Inv.DuplicateSerial");
        (await lots.RecordAsync(companyId, phonesIn, [new LotQuantity("S-1", 1m), new LotQuantity("S-2", 1m)])).IsSuccess.Should().BeTrue();
        (await lots.RecordAsync(companyId, await Receive(phone, 1m, 100m), [new LotQuantity("S-1", 1m)])).Error.Code.Should().Be("Inv.SerialInStock");
        (await lots.RecordAsync(companyId, await Issue(phone, 1m), [new LotQuantity("S-1", 1m)])).IsSuccess.Should().BeTrue();

        (await inventory.AdjustAsync(companyId, milk.Id, store.Id, new DateOnly(2026, 3, 6), 2m, "7500", "ADJ-1", "THEFT")).Error.Code.Should().Be("Inv.UnknownReason");
        (await inventory.AdjustAsync(companyId, milk.Id, store.Id, new DateOnly(2026, 3, 6), 2m, "7500", "ADJ-1", "damaged")).Value.Should().Be(-2m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31), ["Days"] = 30 };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("INV82")).Should().Contain("DAMAGED,Damaged in store,1,0,1,-2");
        (await Render("INV100")).Should().Contain("Uncategorised,DAMAGED Damaged in store,1,0.00,-,2");
        (await Render("INV88")).Should().Contain("LMILK Milk powder,LWH,B1,2026-03-01,2026-04-10,1,2").And.Contain("LMILK Milk powder,LWH,B2,2026-03-01,2026-08-28,2,4");
        string serials = await Render("INV89");
        serials.Should().Contain("LPHONE Handset,S-1,Out,LWH,2026-03-01,2026-03-05,Issue ISS").And.Contain("LPHONE Handset,S-2,In stock,LWH,2026-03-01,2026-03-01,Receipt");
        string expiry = await Render("INV90");
        expiry.Should().Contain("LMILK Milk powder,LWH,B1,2026-04-10,10,Expiring,1,2").And.NotContain("B2");
    }
}
