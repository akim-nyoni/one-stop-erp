using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers refunds from the drawer and exchanges: a cash refund paid from an open session lowers the cash the drawer should hold; an exchange
/// takes goods back towards new ones so only the difference moves (paid in, or given back); returning more than was sold refuses the whole
/// exchange, leaving no new sale; refunds cannot be keyed as till cash movements by hand.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TillExchangeTests
{
    // A cashier id no user created by the test can be given, so a supervisor is never the cashier by accident.
    private const long Cashier = 900_005;

    private readonly AccountingFixture _fixture;

    public TillExchangeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Refunds_and_exchanges_keep_the_drawer_right()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "EX", "Exchange Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal till = new(companyId, branch.Id, "EXT1", "Exchange Till");
        Item soda = new(companyId, "EX-SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Item chips = new(companyId, "EX-CHIPS", "Chips") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "EXW", "Shop");
        db.PosTerminals.Add(till);
        db.Items.AddRange(soda, chips);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        DateOnly day = new(2026, 3, 2);
        long session = (await pos.OpenTillAsync(companyId, till.Id, cashierUserId: Cashier, openingFloat: 100m)).Value;
        async Task<long> Sell(string number, decimal quantity) => (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = till.Id, TillSessionId = session, CashierUserId = Cashier, SaleNumber = number, SaleDate = day, WarehouseId = wh.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(soda.Id, "Soda", quantity, 10m, quantity * 10m, 0m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, quantity * 10m, "1000", IsChange: false)],
        })).Value.SaleId;
        long supervisor = await Scenario.UserWithAsync(db, "ex-supervisor", "Pos.Refund", Core.Security.OperationFlags.AuthoriseLevel1);
        long first = await Sell("EX-1", 2m);
        long firstLine = await db.Set<SaleLine>().Where(l => l.SaleId == first).Select(l => l.Id).SingleAsync();

        // A cash refund of one soda out of the drawer.
        (await events.RefundAsync(new PosRefundRequest
        {
            CompanyId = companyId, SaleId = first, TerminalId = till.Id, CashierUserId = Cashier, AuthorisedByUserId = supervisor, Date = day, RefundAccountCode = "1000", Reason = "Flat",
            Lines = [new PosRefundLine(firstLine, 1m)], TillSessionId = session,
        })).Value.Gross.Should().Be(10m);

        PosExchangeRequest Exchange(long saleId, long lineId, decimal back, string number, decimal chipsPrice) => new()
        {
            CompanyId = companyId, OriginalSaleId = saleId, TerminalId = till.Id, TillSessionId = session, CashierUserId = Cashier, AuthorisedByUserId = supervisor, Date = day, Reason = "Wrong flavour",
            Returns = [new PosRefundLine(lineId, back)], Restock = false, CashAccountCode = "1000", TaxAccountCode = "2200", NewSaleNumber = number, WarehouseId = wh.Id,
            NewLines = [new PosSaleLineRequest(chips.Id, "Chips", 1m, chipsPrice, chipsPrice, 0m, "4000")],
        };

        // The other soda (10) towards chips at 25: the customer pays 15.
        PosExchangeResult paidIn = (await events.ExchangeAsync(Exchange(first, firstLine, 1m, "EX-2", 25m))).Value;
        (paidIn.Returned, paidIn.NewSaleGross, paidIn.Difference).Should().Be((10m, 25m, 15m));

        // Nothing is left to return on EX-1: the exchange is refused whole and EX-3 never exists.
        (await events.ExchangeAsync(Exchange(first, firstLine, 1m, "EX-3", 25m))).Error.Code.Should().Be("Pos.RefundExceedsSale");
        (await db.Sales.AnyAsync(s => s.CompanyId == companyId && s.SaleNumber == "EX-3")).Should().BeFalse();

        // Three sodas (30) towards chips at 25: 5 given back.
        long second = await Sell("EX-4", 3m);
        long secondLine = await db.Set<SaleLine>().Where(l => l.SaleId == second).Select(l => l.Id).SingleAsync();
        (await events.ExchangeAsync(Exchange(second, secondLine, 3m, "EX-5", 25m))).Value.Difference.Should().Be(-5m);

        // The drawer: 100 + 20 − 10 + 15 + 30 − 5 = 150.
        IReadOnlyList<CurrencyCashUp> position = (await pos.ExpectedCashAsync(session)).Value;
        position.Single().Expected.Should().Be(150m);

        (await scope.ServiceProvider.GetRequiredService<ITillCashService>().RecordAsync(new TillCashRequest(session, TillCashKind.Refund, 5m, "1000", "4000", "Keyed refund", Cashier, 9, day)))
            .Error.Code.Should().Be("Pos.RefundThroughService");
    }
}
