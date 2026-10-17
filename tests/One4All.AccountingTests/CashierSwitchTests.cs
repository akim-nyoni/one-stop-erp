using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the quick cashier switch (brief §4.1 "fast cashier switch without closing the sale"): the open session passes to the next cashier with no
/// count and no cash-up, the switch is a till event, sales then carry the new cashier, and a closed session or the same cashier is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CashierSwitchTests
{
    private readonly AccountingFixture _fixture;

    public CashierSwitchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_till_passes_to_the_next_cashier_without_a_cash_up()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "CS", "Switch Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "CSW1", "Switch Till");
        Item item = new(companyId, "CS-TEA", "Tea") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "CSW", "Shop");
        db.AddRange(terminal, item, wh);
        await db.SaveChangesAsync();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        DateOnly day = new(2026, 3, 2);
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 5, openingFloat: 50m)).Value;

        (await pos.SwitchCashierAsync(session, 5, day)).Error.Code.Should().Be("Pos.SameCashier");
        (await pos.SwitchCashierAsync(session, 6, day)).IsSuccess.Should().BeTrue();
        (await db.Set<TillSession>().AsNoTracking().SingleAsync(s => s.Id == session)).CashierUserId.Should().Be(6, "the same session carries on in the new cashier's name");
        PosEvent switched = await db.PosEvents.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.Kind == PosEventKind.CashierSwitch);
        (switched.CashierUserId, switched.TillSessionId).Should().Be((6L, session));
        switched.Description.Should().Contain("from user 5 to user 6");

        var sale = await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 6, SaleNumber = "CS-1", SaleDate = day, WarehouseId = wh.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(item.Id, "Tea", 1m, 4m, 4m, 0m, "4000")], Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 4m, "1000", IsChange: false)],
        });
        sale.IsSuccess.Should().BeTrue(sale.IsFailure ? sale.Error.Message : null);
        (await db.Set<Sale>().AsNoTracking().SingleAsync(s => s.Id == sale.Value.SaleId)).CashierUserId.Should().Be(6);

        (await pos.CloseTillAsync(session)).IsSuccess.Should().BeTrue();
        (await pos.SwitchCashierAsync(session, 7, day)).Error.Code.Should().Be("Pos.SessionClosed");
        (await pos.SwitchCashierAsync(999_999, 7, day)).Error.Code.Should().Be("Pos.SessionNotFound");
    }
}
