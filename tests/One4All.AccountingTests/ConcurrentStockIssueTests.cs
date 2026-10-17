using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers two stock issues of the same item racing: while one issue holds its transaction (and the stock balance and
/// journal sequence rows) open, a second issue reads the balance as it was, loses the race when the first commits and
/// is retried against the new balance - both issues stand, on-hand is right and agrees with the movements.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ConcurrentStockIssueTests
{
    private readonly AccountingFixture _fixture;

    public ConcurrentStockIssueTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_stock_issue_that_loses_the_race_is_retried()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item item = new(companyId, "CSI-ITEM", "Widget") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "CSIWH", "Main");
            db.AddRange(item, wh);
            await db.SaveChangesAsync();
            (itemId, warehouseId) = (item.Id, wh.Id);
            (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 5m, CreditAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();
        }

        using SemaphoreSlim firstHoldsTheLock = new(0);
        Task<decimal> first = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            Result<StockMovementResult> issued = await scope.ServiceProvider.GetRequiredService<IInventoryService>()
                .IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Quantity = 3m });
            firstHoldsTheLock.Release();
            await Task.Delay(1_500);
            await transaction.CommitAsync();
            return issued.Value.Value;
        });

        await firstHoldsTheLock.WaitAsync();
        Task<Result<StockMovementResult>> second = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IInventoryService>()
                .IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Quantity = 4m });
        });

        (await first).Should().Be(15m);
        Result<StockMovementResult> secondResult = await second;
        secondResult.IsSuccess.Should().BeTrue(secondResult.IsFailure ? secondResult.Error.ToString() : null);
        secondResult.Value.Value.Should().Be(20m);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == itemId)).QuantityOnHand.Should().Be(3m);
            LedgerIntegrityReport check = (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value;
            check.Issues.Should().BeEmpty(string.Join("; ", check.Issues.Select(i => $"{i.Check} {i.Subject}: {i.Detail}")));
        }
    }
}
