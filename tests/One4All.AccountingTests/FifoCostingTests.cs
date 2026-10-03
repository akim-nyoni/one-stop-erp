using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers FIFO costing through every stock path: receipts of 10 at 5 and 10 at 8; an issue of 15 costs 50 + 40; its
/// reversal returns 15 at 90 as a new layer; a transfer of 8 carries the layers it takes (5 at 8 and 3 at 6) to the other
/// warehouse; a stocktake shortage of 2 costs 12; an issue of 10 there, allowed below zero, costs its 8 layered units
/// (58) plus 2 at the average (14.50); the next receipt of 4 at 10 settles the deficit, leaving one layer of 2 worth 25.50.
/// The layers always add up to the stock balances, and the ledger and check data agree.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FifoCostingTests
{
    private readonly AccountingFixture _fixture;

    public FifoCostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Fifo_items_are_costed_from_their_oldest_layers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item widget = new(companyId, "FIFO-W", "Widget", ItemType.Stock, CostingMethod.Fifo) { CostOfSalesAccountCode = "5000" };
        Warehouse a = new(companyId, "FIFA", "Main");
        Warehouse b = new(companyId, "FIFB", "Branch");
        db.AddRange(widget, a, b);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        await Receive(inventory, companyId, widget.Id, a.Id, new DateOnly(2026, 3, 1), 10m, 5m, "R1");
        await Receive(inventory, companyId, widget.Id, a.Id, new DateOnly(2026, 3, 5), 10m, 8m, "R2");

        StockMovementResult issue = (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = widget.Id, WarehouseId = a.Id, Date = new DateOnly(2026, 3, 10), Quantity = 15m })).Value;
        issue.Value.Should().Be(90m);
        (await Balance(db, widget.Id, a.Id)).Should().Be((5m, 40m));

        (await inventory.ReverseIssueAsync(companyId, issue.MovementId, new DateOnly(2026, 3, 11))).Value.Value.Should().Be(90m);
        (await inventory.TransferAsync(companyId, widget.Id, a.Id, b.Id, new DateOnly(2026, 3, 12), 8m)).Value.Should().Be(58m);
        (await Layers(db, widget.Id, b.Id)).Should().Equal((new DateOnly(2026, 3, 5), 5m, 40m), (new DateOnly(2026, 3, 11), 3m, 18m));

        (await inventory.AdjustAsync(companyId, widget.Id, a.Id, new DateOnly(2026, 3, 13), 10m, "5300")).Value.Should().Be(-12m);
        (await Balance(db, widget.Id, a.Id)).Should().Be((10m, 60m));

        (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = widget.Id, WarehouseId = b.Id, Date = new DateOnly(2026, 3, 14), Quantity = 10m, AllowNegative = true })).Value.Value.Should().Be(72.5m);
        (await Balance(db, widget.Id, b.Id)).Should().Be((-2m, -14.5m));
        await Receive(inventory, companyId, widget.Id, b.Id, new DateOnly(2026, 3, 15), 4m, 10m, "R3");
        (await Balance(db, widget.Id, b.Id)).Should().Be((2m, 25.5m));
        (await Layers(db, widget.Id, b.Id)).Should().Equal((new DateOnly(2026, 3, 15), 2m, 25.5m));

        (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1400").Debit.Should().Be(85.5m);
        tb.Rows.Single(r => r.AccountCode == "5000").Debit.Should().Be(72.5m);
        tb.Rows.Single(r => r.AccountCode == "5300").Debit.Should().Be(12m);
    }

    private static async Task Receive(IInventoryService inventory, long companyId, long itemId, long warehouseId, DateOnly date, decimal quantity, decimal unitCost, string reference)
        => (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = date, Quantity = quantity, UnitCost = unitCost, CreditAccountCode = "1100", Reference = reference,
        })).IsSuccess.Should().BeTrue();

    private static async Task<(decimal Quantity, decimal Value)> Balance(One4AllDbContext db, long itemId, long warehouseId)
    {
        StockBalance balance = await db.StockBalances.AsNoTracking().SingleAsync(x => x.ItemId == itemId && x.WarehouseId == warehouseId);
        return (balance.QuantityOnHand, balance.TotalValue);
    }

    private static async Task<List<(DateOnly, decimal, decimal)>> Layers(One4AllDbContext db, long itemId, long warehouseId)
        => [.. (await db.Set<StockCostLayer>().AsNoTracking()
                .Where(l => l.ItemId == itemId && l.WarehouseId == warehouseId && l.RemainingQuantity > 0m)
                .OrderBy(l => l.LayerDate).ThenBy(l => l.Id).ToListAsync())
            .Select(l => (l.LayerDate, l.RemainingQuantity, l.RemainingValue))];
}
