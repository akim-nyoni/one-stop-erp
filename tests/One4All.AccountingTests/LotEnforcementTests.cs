using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers enforced batch tracking end to end: a receipt of a batch-tracked item without batches is refused; a GRN carries them; an issue
/// without batches takes the earliest-expiry batch, and one beyond the known batches is refused; a till sale takes batches by expiry; the
/// recall trace leads from a batch back to the supplier and forward to the customers it reached, with what is left on hand.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LotEnforcementTests
{
    private readonly AccountingFixture _fixture;

    public LotEnforcementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Batches_are_captured_enforced_and_traced_for_a_recall()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).EnforceLotTracking = true;
        Supplier dairy = new(companyId, "LT-SUP", "Dairy Co", Scenario.Usd);
        Customer school = new(companyId, "LT-CUS", "Hill School", Scenario.Usd);
        Item milk = new(companyId, "LT-MILK", "Milk") { Tracking = ItemTracking.Batch, CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse store = new(companyId, "LT-WH", "Cold Room");
        Branch branch = new(companyId, "LT", "Shop");
        db.AddRange(dairy, school, milk, store, branch);
        await db.SaveChangesAsync();
        PosTerminal till = new(companyId, branch.Id, "LT-T1", "Till");
        db.PosTerminals.Add(till);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        DateOnly day = new(2026, 4, 1);
        (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = milk.Id, WarehouseId = store.Id, Date = day, Quantity = 5m, UnitCost = 1m, CreditAccountCode = "1100" }))
            .Error.Code.Should().Be("Inv.LotsRequired");

        // Two batches arrive on a GRN: B-LATE expires after B-SOON.
        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long order = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = dairy.Id, OrderNumber = "LT-PO1", OrderDate = day, Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(milk.Id, "Milk", 10m, 1m)],
        })).Value.PurchaseOrderId;
        long line = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == order).Select(l => l.Id).SingleAsync();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = order, WarehouseId = store.Id, GrnNumber = "LT-GRN1", Date = day,
            Lines = [new ReceiveLine(line, 10m, [new LotQuantity("B-LATE", 6m, new DateOnly(2026, 6, 1)), new LotQuantity("B-SOON", 4m, new DateOnly(2026, 4, 20))])],
        })).IsSuccess.Should().BeTrue();

        // An issue to the school's invoice without batches takes the soonest-expiring batch first.
        (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = milk.Id, WarehouseId = store.Id, Date = day.AddDays(1), Quantity = 3m, Reference = "LT-INV1" }))
            .IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.ISalesLedgerService>().PostInvoiceAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = school.Id, DocumentNumber = "LT-INV1", DocumentDate = day.AddDays(1), Currency = Scenario.Usd, NetAmount = 6m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = milk.Id, WarehouseId = store.Id, Date = day.AddDays(1), Quantity = 50m, Reference = "LT-BIG", AllowNegative = true }))
            .Error.Code.Should().Be("Inv.LotsShort");

        // A till sale of 2 takes the last of B-SOON and one of B-LATE.
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, till.Id, 5, 0m)).Value;
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = till.Id, TillSessionId = session, CashierUserId = 5, SaleNumber = "LT-S1", SaleDate = day.AddDays(2), WarehouseId = store.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(milk.Id, "Milk", 2m, 2m, 4m, 0m, "4000")], Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 4m, "1000", IsChange: false)],
        })).IsSuccess.Should().BeTrue();

        ILotTrackingService lots = scope.ServiceProvider.GetRequiredService<ILotTrackingService>();
        LotTrace soon = (await lots.TraceAsync(companyId, milk.Id, "B-SOON")).Value;
        soon.CameFrom.Single().Party.Should().Be("Dairy Co");
        soon.WentTo.Select(m => (m.Reference, m.Quantity, m.Party)).Should().Equal(("LT-INV1", -3m, "Hill School"), ("LT-S1", -1m, "Cash sale"));
        soon.OnHand.Should().BeEmpty();
        LotTrace late = (await lots.TraceAsync(companyId, milk.Id, "B-LATE")).Value;
        late.OnHand.Should().Equal(new Dictionary<long, decimal> { [store.Id] = 5m });
        late.Customers.Should().Equal("Cash sale");
    }
}
