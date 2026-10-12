using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers goods held for suppliers (consignment, bought when used) and customers (never ours), kept out of our stock value.</summary>
[Collection(AccountingCollection.Name)]
public sealed class HeldStockTests
{
    private readonly AccountingFixture _fixture;

    public HeldStockTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Goods_held_for_others_stay_out_of_stock_until_used()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item oil = new(companyId, "HS-OIL", "Engine oil");
        Warehouse main = new(companyId, "HS-MAIN", "Main store");
        db.AddRange(oil, main);
        db.Suppliers.Add(new Supplier(companyId, "HS-SUP", "Lubes Ltd", Scenario.Usd));
        db.Customers.Add(new Customer(companyId, "HS-CUS", "Farmer Moyo", Scenario.Usd));
        await db.SaveChangesAsync();
        (long itemId, long warehouseId) = (oil.Id, main.Id);

        IHeldStockService held = scope.ServiceProvider.GetRequiredService<IHeldStockService>();
        DateOnly date = new(2026, 3, 5);
        (await held.TakeInAsync(companyId, new HeldStockIn(HeldStockOwner.Supplier, "HS-SUP", "HS-OIL", "HS-MAIN", 10m, date, "DN-1"))).Error.Code.Should().Be("Inv.HeldPrice");
        long consignment = (await held.TakeInAsync(companyId, new HeldStockIn(HeldStockOwner.Supplier, "hs-sup", "HS-OIL", "HS-MAIN", 10m, date, "DN-1") { UnitPrice = 4m })).Value;
        long keeping = (await held.TakeInAsync(companyId, new HeldStockIn(HeldStockOwner.Customer, "HS-CUS", "HS-OIL", "HS-MAIN", 3m, date, "JOB-9"))).Value;

        // Nothing is ours yet.
        decimal OnHand() => db.StockBalances.AsNoTracking().Where(b => b.ItemId == itemId && b.WarehouseId == warehouseId).Select(b => b.QuantityOnHand).FirstOrDefault();
        OnHand().Should().Be(0m);

        (await held.UseAsync(companyId, keeping, 1m, date, "USE-X", "2150")).Error.Code.Should().Be("Inv.CustomerOwned");
        (await held.UseAsync(companyId, consignment, 11m, date, "USE-1", "2150")).Error.Code.Should().Be("Inv.HeldShort");
        string journal = (await held.UseAsync(companyId, consignment, 4m, date.AddDays(3), "CONS-USE-1", "2150")).Value;
        journal.Should().NotBeNullOrEmpty();
        OnHand().Should().Be(4m);
        decimal owed = await db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.DocumentNumber == "CONS-USE-1").SumAsync(o => o.OriginalTransaction);
        Math.Abs(owed).Should().Be(16m);

        (await held.ReturnAsync(companyId, consignment, 6m, date.AddDays(5), "RET-1")).IsSuccess.Should().BeTrue();
        (await held.ReturnAsync(companyId, keeping, 3m, date.AddDays(5), "COLLECTED")).IsSuccess.Should().BeTrue();
        (await held.HoldingsAsync(companyId, null)).Should().BeEmpty();
        HeldStock history = await db.Set<HeldStock>().AsNoTracking().Include(h => h.Movements).SingleAsync(h => h.Id == consignment);
        history.Movements.Select(m => m.Kind).Should().Equal(HeldStockMovementKind.In, HeldStockMovementKind.Used, HeldStockMovementKind.Returned);
    }
}
