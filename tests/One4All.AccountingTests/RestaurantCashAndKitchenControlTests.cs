using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Restaurant;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Restaurant cash and kitchen controls: a bill's cash goes into an open till session and reads in its X-read and expected cash; voiding food already
/// fired to the kitchen (or cancelling its order) needs a supervisor other than the waiter holding Pos.Restaurant void / reverse and writes the food
/// off as kitchen wastage; reversing a bill needs such a supervisor too.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RestaurantCashAndKitchenControlTests
{
    private const long Waiter = 900_711;

    private readonly AccountingFixture _fixture;

    public RestaurantCashAndKitchenControlTests(AccountingFixture fixture) => _fixture = fixture;

    private sealed record Setup(long CompanyId, long WarehouseId, long BunId, long BurgerId, long SupervisorId, long ClerkId);

    // A company with a kitchen, a bun (stocked at 2.00), a burger (one bun, 10.00), a supervisor holding Pos.Restaurant void / reverse and a clerk without it.
    private static async Task<Setup> SeedAsync(IServiceProvider services, string prefix)
    {
        One4AllDbContext db = services.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item bun = new(companyId, $"{prefix}-BUN", "Bun") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse kitchen = new(companyId, $"{prefix}K", "Kitchen");
        db.AddRange(bun, kitchen);
        Role supervisors = new($"{prefix} supervisors {companyId}");
        supervisors.Grant("Pos.Restaurant", OperationFlags.VoidReverse);
        Role clerks = new($"{prefix} clerks {companyId}");
        clerks.Grant("Pos.Restaurant", OperationFlags.Create);
        db.Roles.AddRange(supervisors, clerks);
        await db.SaveChangesAsync();
        User supervisor = new($"{prefix}-super-{companyId}", "Floor Supervisor");
        supervisor.AssignRole(supervisors.Id);
        User clerk = new($"{prefix}-clerk-{companyId}", "Clerk");
        clerk.AssignRole(clerks.Id);
        db.Users.AddRange(supervisor, clerk);
        await db.SaveChangesAsync();

        await services.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = bun.Id, WarehouseId = kitchen.Id, Date = new DateOnly(2026, 5, 1), Quantity = 50m, UnitCost = 2m, CreditAccountCode = "1100",
        });
        long burger = (await services.GetRequiredService<IRestaurantService>().CreateMenuItemAsync(companyId, $"{prefix}-BURGER", "Burger", 10m, "4000", "5000",
            [new RecipeIngredient(bun.Id, 1m)])).Value;
        return new Setup(companyId, kitchen.Id, bun.Id, burger, supervisor.Id, clerk.Id);
    }

    [SkippableFact]
    public async Task Bill_cash_needs_an_open_till_session_and_reads_in_its_x_read()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Setup s = await SeedAsync(scope.ServiceProvider, "RCT");
        IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        DateOnly day = new(2026, 5, 2);

        long order = (await res.OpenOffPremiseOrderAsync(s.CompanyId, "RCT-1", OrderType.Takeaway, Waiter, day)).Value;
        (await res.AddItemAsync(s.CompanyId, order, s.BurgerId, 2m)).IsSuccess.Should().BeTrue();

        // No till session: the cash would never be counted at a cash-up.
        (await res.BillOrderAsync(s.CompanyId, order, s.WarehouseId, day, "1000")).Error.Code.Should().Be("Res.TillSessionRequired");

        // A cashed-up session is refused too.
        long till = await RestaurantTill.SessionAsync(scope.ServiceProvider, s.CompanyId);
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        TillSession closed = new(s.CompanyId, (await db.TillSessions.FirstAsync(x => x.Id == till)).TerminalId, 900_712, DateTimeOffset.UtcNow, 0m);
        closed.Close(DateTimeOffset.UtcNow, 1);
        db.TillSessions.Add(closed);
        await db.SaveChangesAsync();
        (await res.BillOrderAsync(s.CompanyId, order, s.WarehouseId, day, "1000", tillSessionId: closed.Id)).Error.Code.Should().Be("Res.TillSessionClosed");

        // In the open session the bill posts and the session reads it: 20.00 gross, all cash.
        Core.Common.Result<BillResult> bill = await res.BillOrderAsync(s.CompanyId, order, s.WarehouseId, day, "1000", tillSessionId: till);
        bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
        (await db.RestaurantBills.AsNoTracking().SingleAsync(b => b.TableOrderId == order)).TillSessionId.Should().Be(till);

        // A split bill: 15 on card needs no drawer, the 5 cash goes into the till.
        long second = (await res.OpenOffPremiseOrderAsync(s.CompanyId, "RCT-2", OrderType.Takeaway, Waiter, day)).Value;
        (await res.AddItemAsync(s.CompanyId, second, s.BurgerId, 2m)).IsSuccess.Should().BeTrue();
        (await res.BillWithTendersAsync(s.CompanyId, second, s.WarehouseId, day, [new TenderLine("1100", 15m, TenderType.Card), new TenderLine("1000", 5m)]))
            .Error.Code.Should().Be("Res.TillSessionRequired");
        (await res.BillWithTendersAsync(s.CompanyId, second, s.WarehouseId, day, [new TenderLine("1100", 15m, TenderType.Card), new TenderLine("1000", 5m)], tillSessionId: till))
            .IsSuccess.Should().BeTrue();

        TillRead x = (await pos.XReadAsync(till)).Value;
        x.RestaurantBillCount.Should().Be(2);
        x.RestaurantGross.Should().Be(40m);
        x.RestaurantCash.Should().Be(25m);
        x.TotalGross.Should().Be(40m);
        x.TenderTotals[TenderType.Cash].Should().Be(25m);
        (await pos.ExpectedCashAsync(till)).Value.Single(c => c.Currency == "USD").Expected.Should().Be(25m);
    }

    [SkippableFact]
    public async Task Voiding_fired_food_needs_a_supervisor_and_posts_kitchen_wastage()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Setup s = await SeedAsync(scope.ServiceProvider, "RKW");
        IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        DateOnly day = new(2026, 5, 3);
        KitchenWastage kitchen = new(s.WarehouseId, "5200", day);

        long order = (await res.OpenOffPremiseOrderAsync(s.CompanyId, "RKW-1", OrderType.Takeaway, Waiter, day)).Value;
        (await res.AddItemAsync(s.CompanyId, order, s.BurgerId, 3m)).IsSuccess.Should().BeTrue();
        (await res.FireOrderAsync(s.CompanyId, order, "RKW-T1", day)).IsSuccess.Should().BeTrue();
        (await res.AddItemAsync(s.CompanyId, order, s.BurgerId, 1m)).IsSuccess.Should().BeTrue(); // not fired
        List<TableOrderLine> lines = await db.Set<TableOrderLine>().AsNoTracking().Where(l => l.TableOrderId == order).OrderBy(l => l.Id).ToListAsync();
        long fired = lines[0].Id, unfired = lines[1].Id;

        // An unfired line is a plain correction: no supervisor, no wastage.
        (await res.VoidLineAsync(s.CompanyId, order, unfired, "Keyed twice")).IsSuccess.Should().BeTrue();

        // A fired line: no supervisor, the waiter, or a user without the right - refused; a supervisor without the kitchen to write it off from - refused.
        (await res.VoidLineAsync(s.CompanyId, order, fired, "Sent back", null, kitchen)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.VoidLineAsync(s.CompanyId, order, fired, "Sent back", Waiter, kitchen)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.VoidLineAsync(s.CompanyId, order, fired, "Sent back", s.ClerkId, kitchen)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.VoidLineAsync(s.CompanyId, order, fired, "Sent back", s.SupervisorId)).Error.Code.Should().Be("Res.WastageRequired");
        (await db.Set<TableOrderLine>().AsNoTracking().SingleAsync(l => l.Id == fired)).IsVoided.Should().BeFalse();

        // With the supervisor: voided, and the three buns made are written off at 2.00 each.
        (await res.VoidLineAsync(s.CompanyId, order, fired, "Sent back", s.SupervisorId, kitchen)).IsSuccess.Should().BeTrue();
        (await db.Set<TableOrderLine>().AsNoTracking().SingleAsync(l => l.Id == fired)).AuthorizedByUserId.Should().Be(s.SupervisorId);
        WastageRecord waste = await db.Set<WastageRecord>().AsNoTracking().SingleAsync(w => w.CompanyId == s.CompanyId);
        waste.ItemId.Should().Be(s.BunId);
        waste.Quantity.Should().Be(3m);
        waste.Value.Should().Be(6m);
        waste.WastageAccountCode.Should().Be("5200");

        // Cancelling an order with fired food: the same supervisor rule, and the food is written off.
        long other = (await res.OpenOffPremiseOrderAsync(s.CompanyId, "RKW-2", OrderType.Takeaway, Waiter, day)).Value;
        (await res.AddItemAsync(s.CompanyId, other, s.BurgerId, 2m)).IsSuccess.Should().BeTrue();
        (await res.FireOrderAsync(s.CompanyId, other, "RKW-T2", day)).IsSuccess.Should().BeTrue();
        (await res.CancelOrderAsync(s.CompanyId, other, "Guests walked out")).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.CancelOrderAsync(s.CompanyId, other, "Guests walked out", s.ClerkId, kitchen)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.CancelOrderAsync(s.CompanyId, other, "Guests walked out", s.SupervisorId, kitchen)).IsSuccess.Should().BeTrue();
        TableOrder cancelled = await db.Set<TableOrder>().AsNoTracking().SingleAsync(o => o.Id == other);
        cancelled.Status.Should().Be(TableOrderStatus.Cancelled);
        cancelled.CancelAuthorisedByUserId.Should().Be(s.SupervisorId);
        (await db.Set<WastageRecord>().AsNoTracking().Where(w => w.CompanyId == s.CompanyId).SumAsync(w => w.Quantity)).Should().Be(5m);
    }

    [SkippableFact]
    public async Task Reversing_a_bill_needs_a_supervisor_other_than_the_operator()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Setup s = await SeedAsync(scope.ServiceProvider, "RBA");
        IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        DateOnly day = new(2026, 5, 4);
        long till = await RestaurantTill.SessionAsync(scope.ServiceProvider, s.CompanyId);

        long order = (await res.OpenOffPremiseOrderAsync(s.CompanyId, "RBA-1", OrderType.Takeaway, Waiter, day)).Value;
        (await res.AddItemAsync(s.CompanyId, order, s.BurgerId, 1m)).IsSuccess.Should().BeTrue();
        (await res.BillOrderAsync(s.CompanyId, order, s.WarehouseId, day, "1000", tillSessionId: till)).IsSuccess.Should().BeTrue();
        long billId = await db.RestaurantBills.Where(b => b.TableOrderId == order).Select(b => b.Id).SingleAsync();

        (await res.ReverseBillAsync(s.CompanyId, billId, day, "Wrong table", Waiter, null)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.ReverseBillAsync(s.CompanyId, billId, day, "Wrong table", s.SupervisorId, s.SupervisorId)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await res.ReverseBillAsync(s.CompanyId, billId, day, "Wrong table", Waiter, s.ClerkId)).Error.Code.Should().Be("Res.SupervisorRequired");
        (await db.RestaurantBills.AsNoTracking().SingleAsync(b => b.Id == billId)).IsReversed.Should().BeFalse();

        (await res.ReverseBillAsync(s.CompanyId, billId, day, "Wrong table", Waiter, s.SupervisorId)).IsSuccess.Should().BeTrue();
        RestaurantBill reversed = await db.RestaurantBills.AsNoTracking().SingleAsync(b => b.Id == billId);
        reversed.IsReversed.Should().BeTrue();
        reversed.ReversalAuthorisedByUserId.Should().Be(s.SupervisorId);

        // The reversed bill's cash is out of the drawer again.
        TillRead x = (await scope.ServiceProvider.GetRequiredService<IPosService>().XReadAsync(till)).Value;
        x.RestaurantBillCount.Should().Be(0);
        x.RestaurantCash.Should().Be(0m);
    }
}
