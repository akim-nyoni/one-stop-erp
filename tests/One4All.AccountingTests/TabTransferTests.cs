using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers moving lines between bar tabs and merging tabs (brief §4.1): part of a line moves with its share of the tax and stays fired; a merge moves
/// everything unbilled, cancels the emptied tab and frees its table; only open tabs and unbilled quantities move.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TabTransferTests
{
    private readonly AccountingFixture _fixture;

    public TabTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lines_move_between_tabs_and_tabs_merge()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IRestaurantService restaurant = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        DateOnly night = new(2026, 5, 2);
        long beer = (await restaurant.CreateMenuItemAsync(companyId, "TT-BEER", "Beer", 4m, "4000", "5000", [])).Value;
        long table1 = (await restaurant.CreateTableAsync(companyId, "TT1", 4)).Value, table2 = (await restaurant.CreateTableAsync(companyId, "TT2", 4)).Value;
        long first = (await restaurant.OpenOrderAsync(companyId, table1, "TT-1", 1, night, 2)).Value;
        long second = (await restaurant.OpenOrderAsync(companyId, table2, "TT-2", 1, night, 2)).Value;
        (await restaurant.AddItemAsync(companyId, first, beer, 4m, 2.40m)).IsSuccess.Should().BeTrue();
        (await restaurant.FireOrderAsync(companyId, first, "TT-K1", night)).IsSuccess.Should().BeTrue();
        long line = await db.Set<TableOrderLine>().Where(l => l.TableOrderId == first).Select(l => l.Id).SingleAsync();

        // One of the four beers goes to the other tab, with a quarter of the tax, already fired.
        long moved = (await restaurant.TransferLineAsync(companyId, first, line, second, 1m)).Value;
        (await restaurant.TransferLineAsync(companyId, first, line, second, 9m)).Error.Code.Should().Be("Res.CannotMoveLine");
        (await restaurant.TransferLineAsync(companyId, first, line, first, 1m)).Error.Code.Should().Be("Res.SameOrder");
        TableOrderLine copy = await db.Set<TableOrderLine>().AsNoTracking().SingleAsync(l => l.Id == moved);
        (copy.TableOrderId, copy.Quantity, copy.TaxAmount, copy.Fired).Should().Be((second, 1m, 0.60m, true));
        TableOrderLine left = await db.Set<TableOrderLine>().AsNoTracking().SingleAsync(l => l.Id == line);
        (left.Quantity, left.TaxAmount).Should().Be((3m, 1.80m));

        // The second tab merges into the first: its beer moves back, the tab closes and its table is free.
        (await restaurant.MergeOrdersAsync(companyId, second, first)).Value.Should().Be(1);
        TableOrder merged = await db.Set<TableOrder>().AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == first);
        merged.NetTotal.Should().Be(16m);
        merged.TaxTotal.Should().Be(2.40m);
        (await db.Set<TableOrder>().AsNoTracking().SingleAsync(o => o.Id == second)).Status.Should().Be(TableOrderStatus.Cancelled);
        (await db.Set<RestaurantTable>().AsNoTracking().SingleAsync(t => t.Id == table2)).Status.Should().Be(TableStatus.Free);
        (await restaurant.MergeOrdersAsync(companyId, second, first)).Error.Code.Should().Be("Res.OrderNotOpen");
    }
}
