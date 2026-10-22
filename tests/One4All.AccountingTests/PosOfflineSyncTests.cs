using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Offline;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end offline POS sync: a sale captured while offline is enqueued in the database outbox and,
/// when the sync service drains it, posts through the normal POS path. Replaying the same sale number is
/// idempotent (no second journal), and an operation with no handler is dead-lettered.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosOfflineSyncTests
{
    private readonly AccountingFixture _fixture;

    public PosOfflineSyncTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Captured_sale_syncs_once_and_replay_is_idempotent()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long terminalId;
        long warehouseId;
        long itemId;
        long sessionId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch branch = new(companyId, "HO", "Head Office");
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            PosTerminal terminal = new(companyId, branch.Id, "TILL1", "Front Till");
            db.PosTerminals.Add(terminal);
            Item item = new(companyId, "SODA", "Soda 500ml") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "WH1", "Shop Floor");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            terminalId = terminal.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" });

            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value;
        }

        // A sale captured offline: 10 @ 2.00 net 20 + VAT 3 = 23, cash 25 with 2 change.
        PosSaleEnvelope envelope = new(
            companyId, terminalId, sessionId, CashierUserId: 1,
            SaleNumber: "OFF-1", SaleDate: new DateOnly(2026, 3, 2), warehouseId, Currency: "USD", TaxAccountCode: "2200",
            Lines: [new PosSaleLineEnvelope(itemId, "Soda 500ml", 10m, 2.00m, 20m, 3m, "4000")],
            Tenders:
            [
                new PosTenderEnvelope(TenderType.Cash, "USD", 25m, "1000", IsChange: false),
                new PosTenderEnvelope(TenderType.Cash, "USD", 2m, "1000", IsChange: true),
            ]);
        string payload = JsonSerializer.Serialize(envelope);

        // Enqueue two operations for the SAME sale number (as a redelivery would), plus one unknown kind.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOfflineOutbox outbox = scope.ServiceProvider.GetRequiredService<IOfflineOutbox>();
            IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();
            (await outbox.EnqueueAsync(new OfflineOperation(companyId, Guid.NewGuid(), PosSaleEnvelope.Kind, payload, clock.UtcNow))).Should().BeTrue();
            (await outbox.EnqueueAsync(new OfflineOperation(companyId, Guid.NewGuid(), PosSaleEnvelope.Kind, payload, clock.UtcNow.AddSeconds(1)))).Should().BeTrue();
            (await outbox.EnqueueAsync(new OfflineOperation(companyId, Guid.NewGuid(), "MysteryKind", "{}", clock.UtcNow.AddSeconds(2)))).Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOfflineSyncService sync = scope.ServiceProvider.GetRequiredService<IOfflineSyncService>();
            OfflineSyncReport report = await sync.SyncPendingAsync();

            report.Synced.Should().Be(2);       // both PosSale ops report success (second is an idempotent no-op)
            report.DeadLettered.Should().Be(1); // the unknown kind
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // Idempotent: exactly ONE sale posted for the sale number despite two operations.
            int sales = await db.Set<Sale>().CountAsync(s => s.CompanyId == companyId && s.SaleNumber == "OFF-1");
            sales.Should().Be(1);

            // Both PosSale operations are Synced; the mystery one is dead-lettered.
            List<OfflineOperation> ops = await db.OfflineOperations.OrderBy(o => o.CreatedUtc).ToListAsync();
            ops.Where(o => o.Kind == PosSaleEnvelope.Kind).Should().OnlyContain(o => o.Status == OfflineOperationStatus.Synced);
            ops.Single(o => o.Kind == "MysteryKind").Status.Should().Be(OfflineOperationStatus.Failed);

            // Stock reduced once (100 received − 10 sold).
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "SODA" && l.Quantity == 90m);
        }

        // A second drain has nothing left to do.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOfflineSyncService sync = scope.ServiceProvider.GetRequiredService<IOfflineSyncService>();
            (await sync.SyncPendingAsync()).Processed.Should().Be(0);
        }
    }

    [SkippableFact]
    public async Task A_till_opened_offline_syncs_before_its_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "HO", "Head Office");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "OFFTILL", "Offline Till");
        Item item = new(companyId, "OFFSODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "OFFWH", "Shop");
        db.AddRange(terminal, item, wh);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 1m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        string key = $"OFF-{companyId}-T1";
        DateTimeOffset opened = new(2026, 3, 2, 7, 0, 0, TimeSpan.Zero);
        PosSaleEnvelope sale = new(companyId, terminal.Id, 0, 1, $"OFFS-{companyId}", new DateOnly(2026, 3, 2), wh.Id, "USD", "2200",
            [new PosSaleLineEnvelope(item.Id, "Soda", 2m, 2m, 4m, 0m, "4000")], [new PosTenderEnvelope(TenderType.Cash, "USD", 4m, "1000", IsChange: false)], OfflineSessionKey: key);
        IOfflineOutbox outbox = scope.ServiceProvider.GetRequiredService<IOfflineOutbox>();
        IOfflineSyncService sync = scope.ServiceProvider.GetRequiredService<IOfflineSyncService>();

        // The sale alone cannot post until its till has synced.
        (await outbox.EnqueueAsync(new OfflineOperation(companyId, Guid.NewGuid(), PosSaleEnvelope.Kind, JsonSerializer.Serialize(sale), opened.AddMinutes(5)))).Should().BeTrue();
        (await sync.SyncPendingAsync()).Synced.Should().Be(0);

        (await outbox.EnqueueAsync(new OfflineOperation(companyId, Guid.NewGuid(), PosOpenTillEnvelope.Kind,
            JsonSerializer.Serialize(new PosOpenTillEnvelope(companyId, terminal.Id, 1, 50m, opened, key)), opened))).Should().BeTrue();
        await sync.SyncPendingAsync();
        await sync.SyncPendingAsync();

        TillSession session = await db.Set<TillSession>().AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.OfflineKey == key);
        (session.OpeningFloat, session.OpenedOnUtc).Should().Be((50m, opened));
        (await db.Set<Sale>().AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == $"OFFS-{companyId}")).TillSessionId.Should().Be(session.Id);
        (await scope.ServiceProvider.GetRequiredService<IPosService>().OpenTillOfflineAsync(companyId, terminal.Id, 1, 50m, opened, key)).Value.Should().Be(session.Id);
    }

    [SkippableFact]
    public async Task A_void_a_refund_and_the_cash_up_captured_offline_replay_after_their_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "HO", "Head Office");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "OFFTILL2", "Offline Till 2");
        Item item = new(companyId, "OFFTEA", "Tea") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "OFFWH2", "Shop");
        db.AddRange(terminal, item, wh);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 1m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        long supervisor = await Scenario.UserWithAsync(db, "off-supervisor", "Pos.Refund", Core.Security.OperationFlags.AuthoriseLevel1);
        string key = $"OFF2-{companyId}";
        DateOnly day = new(2026, 3, 2);
        DateTimeOffset opened = new(2026, 3, 2, 7, 0, 0, TimeSpan.Zero);
        PosSaleEnvelope SaleOf(string number) => new(companyId, terminal.Id, 0, 1, number, day, wh.Id, "USD", "2200",
            [new PosSaleLineEnvelope(item.Id, "Tea", 2m, 3m, 6m, 0m, "4000")], [new PosTenderEnvelope(TenderType.Cash, "USD", 6m, "1000", IsChange: false)], OfflineSessionKey: key);
        string kept = $"OFFK-{companyId}", voided = $"OFFV-{companyId}", refunded = $"OFFR-{companyId}";
        string reason = "Refund at the till (offline OFR-TEST)";
        IOfflineOutbox outbox = scope.ServiceProvider.GetRequiredService<IOfflineOutbox>();
        IOfflineSyncService sync = scope.ServiceProvider.GetRequiredService<IOfflineSyncService>();
        async Task Enqueue(string kind, object payload, int minute)
            => (await outbox.EnqueueAsync(new OfflineOperation(companyId, Guid.NewGuid(), kind, JsonSerializer.Serialize(payload), opened.AddMinutes(minute)))).Should().BeTrue();

        // The terminal's day, as queued: till opened, three sales, one voided, one refunded, the count declared and the till closed.
        await Enqueue(PosOpenTillEnvelope.Kind, new PosOpenTillEnvelope(companyId, terminal.Id, 1, 50m, opened, key), 0);
        await Enqueue(PosSaleEnvelope.Kind, SaleOf(kept), 1);
        await Enqueue(PosSaleEnvelope.Kind, SaleOf(voided), 2);
        await Enqueue(PosSaleEnvelope.Kind, SaleOf(refunded), 3);
        await Enqueue(PosVoidEnvelope.Kind, new PosVoidEnvelope(companyId, voided, day, "Voided at till (offline)", "2200", 2), 4);
        await Enqueue(PosRefundEnvelope.Kind, new PosRefundEnvelope(companyId, refunded, terminal.Id, 1, supervisor, day, "1000", "2200", reason, 0, key), 5);
        await Enqueue(PosCashUpEnvelope.Kind, new PosCashUpEnvelope(companyId, 0, key, [new PosCashCountEnvelope("USD", 56m)], 1, day, "1000", "6900"), 6);

        OfflineSyncReport first = await sync.SyncPendingAsync();
        first.DeadLettered.Should().Be(0, "everything replays in order");
        while ((await sync.SyncPendingAsync()).Retryable > 0)
        {
            // Operations that depend on an earlier one retry on the next drain.
        }

        TillSession session = await db.Set<TillSession>().AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.OfflineKey == key);
        (session.Status, session.ZReadNumber.HasValue).Should().Be((TillSessionStatus.Closed, true), "the till cashed up and closed after its sales");
        (await db.Set<Sale>().AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == voided)).Status.Should().Be(SaleStatus.Voided);
        (await db.Set<Sale>().AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == kept)).Status.Should().Be(SaleStatus.Completed);
        (await db.PosEvents.AsNoTracking().Where(e => e.CompanyId == companyId && e.SaleNumber == refunded && e.Kind == PosEventKind.Refund).ToListAsync())
            .Should().ContainSingle().Which.Reason.Should().Be(reason);
        (await db.OfflineOperations.AsNoTracking().Where(o => o.CompanyId == companyId).ToListAsync()).Should().OnlyContain(o => o.Status == OfflineOperationStatus.Synced);

        // Redelivered, each is a no-op: no second void, refund or close.
        await Enqueue(PosVoidEnvelope.Kind, new PosVoidEnvelope(companyId, voided, day, "Voided at till (offline)", "2200", 2), 7);
        await Enqueue(PosRefundEnvelope.Kind, new PosRefundEnvelope(companyId, refunded, terminal.Id, 1, supervisor, day, "1000", "2200", reason, 0, key), 8);
        await Enqueue(PosCashUpEnvelope.Kind, new PosCashUpEnvelope(companyId, 0, key, [new PosCashCountEnvelope("USD", 56m)], 1, day, "1000", "6900"), 9);
        (await sync.SyncPendingAsync()).Synced.Should().Be(3);
        (await db.PosEvents.AsNoTracking().CountAsync(e => e.CompanyId == companyId && e.SaleNumber == refunded && e.Kind == PosEventKind.Refund)).Should().Be(1);
    }
}
