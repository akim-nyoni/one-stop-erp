using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers purchase order delivery schedules: a line spread over due dates, receipts matched earliest-due first, and the expediting list of
/// late and soon-due deliveries (unscheduled lines due on the required-by date).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DeliveryScheduleTests
{
    private readonly AccountingFixture _fixture;

    public DeliveryScheduleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Schedules_drive_the_expediting_list()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item bolt = new(companyId, "DS-BOLT", "Bolt"), nut = new(companyId, "DS-NUT", "Nut");
        Warehouse store = new(companyId, "DS-WH", "Store");
        Supplier supplier = new(companyId, "DS-SUP", "Fixings Ltd", Scenario.Usd);
        db.AddRange(bolt, nut, store, supplier);
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long boltOrder = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "DS-PO1", OrderDate = new DateOnly(2026, 6, 1), Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(bolt.Id, "Bolts", 300m, 0.1m)],
        })).Value.PurchaseOrderId;
        (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "DS-PO2", OrderDate = new DateOnly(2026, 6, 1), RequiredBy = new DateOnly(2026, 6, 10), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(nut.Id, "Nuts", 50m, 0.05m)],
        })).IsSuccess.Should().BeTrue();
        long boltLine = db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == boltOrder).Select(l => l.Id).Single();

        IDeliveryScheduleService schedules = scope.ServiceProvider.GetRequiredService<IDeliveryScheduleService>();
        (await schedules.ScheduleAsync(companyId, boltLine, [(new DateOnly(2026, 6, 5), 100m), (new DateOnly(2026, 6, 12), 100m)])).Error.Code.Should().Be("Pop.ScheduleInvalid");
        (await schedules.ScheduleAsync(companyId, boltLine, [(new DateOnly(2026, 5, 30), 300m)])).Error.Code.Should().Be("Pop.ScheduleInvalid");
        (await schedules.ScheduleAsync(companyId, boltLine, [(new DateOnly(2026, 6, 19), 100m), (new DateOnly(2026, 6, 5), 100m), (new DateOnly(2026, 6, 12), 100m)])).IsSuccess.Should().BeTrue();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = boltOrder, WarehouseId = store.Id, GrnNumber = "DS-GRN1", Date = new DateOnly(2026, 6, 6), Lines = [new ReceiveLine(boltLine, 150m)],
        })).IsSuccess.Should().BeTrue();

        // The 150 received clears the 5th and half the 12th; the 19th is a week out.
        (await schedules.ExpediteAsync(companyId, new DateOnly(2026, 6, 13), daysAhead: 7)).Should().Equal(
            new ExpediteLine("DS-PO2", "DS-SUP", "DS-NUT", new DateOnly(2026, 6, 10), 50m, 0m, 50m, 3),
            new ExpediteLine("DS-PO1", "DS-SUP", "DS-BOLT", new DateOnly(2026, 6, 12), 100m, 50m, 50m, 1),
            new ExpediteLine("DS-PO1", "DS-SUP", "DS-BOLT", new DateOnly(2026, 6, 19), 100m, 0m, 100m, -6));
        (await schedules.ExpediteAsync(companyId, new DateOnly(2026, 6, 13))).Should().HaveCount(2);
    }
}
