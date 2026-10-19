using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #15: an inbound load weighed against a purchase order is received on a GRN for its net tonnes when the ticket completes.</summary>
[Collection(AccountingCollection.Name)]
public sealed class WeighbridgeInboundGrnTests
{
    private readonly AccountingFixture _fixture;

    public WeighbridgeInboundGrnTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_inbound_load_against_an_order_is_received_on_a_grn()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier quarry = new(companyId, "WG-Q", "Quarry", Scenario.Usd);
        Supplier other = new(companyId, "WG-O", "Other quarry", Scenario.Usd);
        Item stone = new(companyId, "WG-STONE", "Crushed stone") { CostOfSalesAccountCode = "5000" };
        Warehouse yard = new(companyId, "WG-YD", "Stone yard");
        db.AddRange(quarry, other, stone, yard);
        await db.SaveChangesAsync();
        IProcurementService purchasing = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        (await purchasing.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = quarry.Id, OrderNumber = "WG-PO1", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(stone.Id, "Crushed stone", 30m, 12m)],
        })).IsSuccess.Should().BeTrue();

        IWeighbridgeService bridge = scope.ServiceProvider.GetRequiredService<IWeighbridgeService>();
        FirstWeighRequest Arrive(long supplierId, long? warehouseId) => new()
        {
            CompanyId = companyId, Direction = WeighDirection.Inbound, Vehicle = "WG-TRK", ItemId = stone.Id, CubicMetresPerTonne = 0.7m, Date = new DateOnly(2026, 3, 5), WeightKg = 32_000m,
            SupplierId = supplierId, WarehouseId = warehouseId, PurchaseOrderNumber = "wg-po1", OperatorUserId = 1,
        };
        (await bridge.FirstWeighAsync(Arrive(other.Id, yard.Id))).Error.Code.Should().Be("Wb.OrderMismatch");
        (await bridge.FirstWeighAsync(Arrive(quarry.Id, null))).Error.Code.Should().Be("Wb.ReceiptWarehouse");
        (long ticketId, string ticketNumber) = (await bridge.FirstWeighAsync(Arrive(quarry.Id, yard.Id))).Value;

        // 32 t in, 12 t out empty: 20 t received on the order into the yard, on a GRN numbered after the ticket.
        (await bridge.SecondWeighAsync(new SecondWeighRequest { CompanyId = companyId, TicketId = ticketId, WeightKg = 12_000m, OperatorUserId = 1 })).Value.NetTonnes.Should().Be(20m);
        (await db.WeighbridgeTickets.AsNoTracking().SingleAsync(t => t.Id == ticketId)).GrnNumber.Should().Be(ticketNumber);
        (await db.Set<Core.Procurement.PurchaseOrderLine>().AsNoTracking().SingleAsync(l => l.ItemId == stone.Id)).ReceivedQuantity.Should().Be(20m);
        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == stone.Id && b.WarehouseId == yard.Id)).QuantityOnHand.Should().Be(20m);
    }
}
