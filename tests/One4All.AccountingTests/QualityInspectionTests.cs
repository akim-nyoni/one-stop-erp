using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers quality inspection holds: 10 widgets at 5 received into quarantine are not available to sell; inspection
/// passes 8 into the main store and rejects 2, returned to the supplier for a 10 credit; inspecting more than remains is
/// refused, as is inspecting goods never held; the supplier scorecard counts the 2 returned.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class QualityInspectionTests
{
    private readonly AccountingFixture _fixture;

    public QualityInspectionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Held_goods_are_released_or_returned_on_inspection()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "5190", "Purchase Price Variance", AccountType.Expense));
        Supplier supplier = new(companyId, "QC-SUP", "Widget Works", Scenario.Usd);
        Item widget = new(companyId, "QC-W", "Widget") { CostOfSalesAccountCode = "5000" };
        Warehouse quarantine = new(companyId, "QCHOLD", "Inspection hold") { IsQuarantine = true };
        Warehouse store = new(companyId, "QCMAIN", "Main store");
        db.AddRange(supplier, widget, quarantine, store);
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        async Task<long> Receive(string number, long warehouseId)
        {
            long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(widget.Id, "Widget", 10m, 5m)],
            })).Value.PurchaseOrderId;
            long lineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).Select(l => l.Id).SingleAsync();
            return (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = warehouseId, GrnNumber = number + "-G", Date = new DateOnly(2026, 3, 5), Lines = [new ReceiveLine(lineId, 10m)],
            })).Value.GrnId;
        }

        long held = await Receive("QC-PO1", quarantine.Id);
        IStockAvailabilityService availability = scope.ServiceProvider.GetRequiredService<IStockAvailabilityService>();
        (await availability.AvailabilityAsync(companyId, widget.Id)).Value.Free.Should().Be(0m);

        IQualityInspectionService qc = scope.ServiceProvider.GetRequiredService<IQualityInspectionService>();
        InspectionRequest Inspect(decimal passed, decimal rejected, string? reason) =>
            new(new DateOnly(2026, 3, 6), 4, store.Id, "5190", [new InspectionResult(widget.Id, passed, rejected, reason)]);
        (await qc.InspectAsync(companyId, held, Inspect(8m, 2m, null))).Error.Code.Should().Be("Qc.Invalid");
        (await qc.InspectAsync(companyId, held, Inspect(8m, 2m, "Cracked casings"))).Value.Should().BeEquivalentTo(new { Released = 8m, Returned = 2m, CreditNoteValue = 10m });
        (await qc.InspectAsync(companyId, held, Inspect(1m, 0m, null))).Error.Code.Should().Be("Qc.Exceeds");

        (await availability.AvailabilityAsync(companyId, widget.Id)).Value.Free.Should().Be(8m);
        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == widget.Id && b.WarehouseId == store.Id)).QuantityOnHand.Should().Be(8m);
        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == widget.Id && b.WarehouseId == quarantine.Id)).QuantityOnHand.Should().Be(0m);

        long direct = await Receive("QC-PO2", store.Id);
        (await qc.InspectAsync(companyId, direct, Inspect(1m, 0m, null))).Error.Code.Should().Be("Qc.NotHeld");

        SupplierScore score = (await scope.ServiceProvider.GetRequiredService<ISupplierScorecardService>().ScorecardAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Single();
        (score.QuantityReceived, score.QuantityReturned).Should().Be((20m, 2m));
        (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
    }
}
