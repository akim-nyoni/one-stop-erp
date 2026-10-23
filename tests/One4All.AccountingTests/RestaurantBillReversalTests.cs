using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantBillReversalTests
{
    private const long Waiter = 22501;

    private readonly AccountingFixture _fixture;

    public RestaurantBillReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reversing_a_bill_unwinds_the_revenue_and_returns_the_stock()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long steakItemId;
        long menuId;
        long orderId;
        long billId;
        long supervisorId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Security.Role supervisors = new($"Bill reversers {companyId}");
            supervisors.Grant("Pos.Restaurant", Core.Security.OperationFlags.VoidReverse);
            db.Roles.Add(supervisors);
            await db.SaveChangesAsync();
            Core.Security.User supervisor = new($"br-super-{companyId}", "Bill Supervisor");
            supervisor.AssignRole(supervisors.Id);
            db.Users.Add(supervisor);
            await db.SaveChangesAsync();
            supervisorId = supervisor.Id;
            Item steak = new(companyId, "BR-STEAK", "Steak meat") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(steak);
            Warehouse wh = new(companyId, "BRKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            steakItemId = steak.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = steakItemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 18), Quantity = 50m, UnitCost = 12.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "BR-STEAKPLATE", "Steak Plate", 90m, "4000", "5000", [new RecipeIngredient(steakItemId, 1m)])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "BR1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "BR-ORD-1", Waiter, new DateOnly(2026, 4, 18))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m, taxAmount: 15m);

            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 18), "1000", "2200", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(105m);
            bill.Value.CostOfSales.Should().Be(12m);

            billId = (await db.Set<RestaurantBill>().FirstAsync(b => b.TableOrderId == orderId)).Id;
        }

        // After billing: cash 105, revenue 90, VAT 15, COGS 12, steak stock 49.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "BR-STEAK" && l.Quantity == 49m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "  ", Waiter, supervisorId)).IsFailure.Should().BeTrue(); // reason required
            (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "Rung on wrong table", Waiter, supervisorId)).IsSuccess.Should().BeTrue();
            // Cannot reverse twice.
            (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "again", Waiter, supervisorId)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            RestaurantBill reversed = await db.Set<RestaurantBill>().FirstAsync(b => b.Id == billId);
            reversed.IsReversed.Should().BeTrue();
            reversed.ReversalJournalNumber.Should().NotBeNullOrEmpty();

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "BR-STEAK" && l.Quantity == 50m); // stock returned

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // Everything nets to zero: revenue, VAT, cash, COGS.
            tb.Rows.Should().NotContain(r => r.AccountCode == "4000" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "2200" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "1000" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "5000" && (r.Debit != 0m || r.Credit != 0m));
        }
    }

    [SkippableFact]
    public async Task A_bill_reversed_after_its_session_is_cashed_up_pays_the_cash_out_of_an_open_session_of_the_same_till()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long supervisorId = await Scenario.UserWithAsync(db, "br-z-super", "Pos.Restaurant", Core.Security.OperationFlags.VoidReverse);
        Item steak = new(companyId, "BRZ-STEAK", "Steak meat") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse kitchen = new(companyId, "BRZ-KIT", "Kitchen");
        db.AddRange(steak, kitchen);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = steak.Id, WarehouseId = kitchen.Id, Date = new DateOnly(2026, 4, 18), Quantity = 50m, UnitCost = 12.00m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        long menuId = (await res.CreateMenuItemAsync(companyId, "BRZ-PLATE", "Steak Plate", 90m, "4000", "5000", [new RecipeIngredient(steak.Id, 1m)])).Value;
        long tableId = (await res.CreateTableAsync(companyId, "BRZ1", 4)).Value;
        long orderId = (await res.OpenOrderAsync(companyId, tableId, "BRZ-ORD-1", Waiter, new DateOnly(2026, 4, 18))).Value;
        (await res.AddItemAsync(companyId, orderId, menuId, 1m, taxAmount: 15m)).IsSuccess.Should().BeTrue();
        long session = await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId);
        var bill = await res.BillOrderAsync(companyId, orderId, kitchen.Id, new DateOnly(2026, 4, 18), "1000", "2200", tillSessionId: session);
        bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
        long billId = await db.RestaurantBills.Where(b => b.TableOrderId == orderId).Select(b => b.Id).SingleAsync();

        // The session is cashed up with the bill in it: its Z is signed off at 105 of restaurant cash.
        Application.Abstractions.Pos.IPosService pos = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Pos.IPosService>();
        var closed = await pos.CloseTillAsync(session);
        closed.IsSuccess.Should().BeTrue(closed.IsFailure ? closed.Error.ToString() : null);
        (closed.Value.RestaurantBillCount, closed.Value.RestaurantCash, closed.Value.TotalGross).Should().Be((1, 105m, 105m));

        // Reversing now needs an open session of the same till to pay the cash back out of: none, or another till's, is refused.
        (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "Rung on wrong table", Waiter, supervisorId)).Error.Code.Should().Be("Res.SessionClosed");
        (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "Rung on wrong table", Waiter, supervisorId, session)).Error.Code.Should().Be("Res.SessionClosed");
        long terminalId = await db.TillSessions.Where(s => s.Id == session).Select(s => s.TerminalId).SingleAsync();
        long branchId = await db.PosTerminals.Where(t => t.Id == terminalId).Select(t => t.BranchId).SingleAsync();
        Core.Pos.PosTerminal otherTill = new(companyId, branchId, "RES-TILL2", "Other Till");
        db.PosTerminals.Add(otherTill);
        await db.SaveChangesAsync();
        long otherSession = (await pos.OpenTillAsync(companyId, otherTill.Id, 900_702, 0m)).Value;
        (await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "Rung on wrong table", Waiter, supervisorId, otherSession)).Error.Code.Should().Be("Res.TillSessionOtherTill");
        (await db.RestaurantBills.AsNoTracking().SingleAsync(b => b.Id == billId)).IsReversed.Should().BeFalse();

        // With the till's next session open the cash leaves that drawer as a refund; the signed-off session still reads as it did.
        long next = (await pos.OpenTillAsync(companyId, terminalId, RestaurantTill.CashierUserId, 0m)).Value;
        var reversed = await res.ReverseBillAsync(companyId, billId, new DateOnly(2026, 4, 19), "Rung on wrong table", Waiter, supervisorId, next);
        reversed.IsSuccess.Should().BeTrue(reversed.IsFailure ? reversed.Error.ToString() : null);
        RestaurantBill record = await db.RestaurantBills.AsNoTracking().SingleAsync(b => b.Id == billId);
        (record.IsReversed, record.RefundTillSessionId, record.CashAccountCode).Should().Be((true, (long?)next, "1000"));
        Core.Pos.TillCashMovement refund = await db.TillCashMovements.AsNoTracking().SingleAsync(m => m.TillSessionId == next);
        (refund.Kind, refund.Amount, refund.AccountCode, refund.AuthorisedByUserId).Should().Be((Core.Pos.TillCashKind.Refund, 105m, "1000", (long?)supervisorId));
        (await pos.ExpectedCashAsync(next)).Value.Single(c => c.Currency == "USD").Expected.Should().Be(-105m);
        var signedOff = (await pos.XReadAsync(session)).Value;
        (signedOff.RestaurantBillCount, signedOff.RestaurantCash, signedOff.TotalGross).Should().Be((1, 105m, 105m));
        (await db.TillCashMovements.AsNoTracking().AnyAsync(m => m.TillSessionId == session)).Should().BeFalse();
    }
}
