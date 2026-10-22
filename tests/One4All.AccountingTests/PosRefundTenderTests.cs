using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers how a till refund pays the money back (eleventh-finding audit): through the sale's own tenders by default - a card sale back to the
/// card's account, an on-account sale as a credit note on the customer's account set against the sale's invoice; a different method (cash for a
/// card sale) only with the third-level refund authority; every refund authorised by a holder of the refund right, not merely another user; and
/// a cash refund only out of an open session of the till.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosRefundTenderTests
{
    // A cashier id no user created by the test can be given, so a supervisor is never the cashier by accident.
    private const long Cashier = 900_005;

    private readonly AccountingFixture _fixture;

    public PosRefundTenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Refunds_go_back_the_way_the_sale_was_paid()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "RT", "Refund Tender Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal till = new(companyId, branch.Id, "RTT1", "Refund Tender Till");
        Warehouse shop = new(companyId, "RT-WH", "Shop");
        Item soda = new(companyId, "RT-SODA", "Soda") { RevenueAccountCode = "4000" };
        Customer builder = new(companyId, "RT-BLD", "Builder on account", Scenario.Usd);
        db.AddRange(till, shop, soda, builder);
        await db.SaveChangesAsync();

        long lead = await Scenario.UserWithAsync(db, "rt-lead", "Pos.Refund", OperationFlags.AuthoriseLevel1);
        long manager = await Scenario.UserWithAsync(db, "rt-manager", "Pos.Refund", OperationFlags.AuthoriseLevel1 | OperationFlags.AuthoriseLevel3);
        long clerk = await Scenario.UserWithAsync(db, "rt-clerk", "Pos.Sale", OperationFlags.View | OperationFlags.Create);

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        DateOnly day = new(2026, 6, 2);
        long session = (await pos.OpenTillAsync(companyId, till.Id, cashierUserId: Cashier, openingFloat: 100m)).Value;
        async Task<(long Sale, long Line)> Sell(string number, decimal quantity, PosTenderRequest tender, long? customer = null)
        {
            Result<CompleteSaleResult> sold = await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = till.Id, TillSessionId = session, CashierUserId = Cashier, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id, CustomerId = customer,
                Lines = [new PosSaleLineRequest(soda.Id, "Soda", quantity, 10m, quantity * 10m, 0m, "4000")], Tenders = [tender],
            });
            sold.IsSuccess.Should().BeTrue(sold.IsFailure ? sold.Error.Message : null);
            return (sold.Value.SaleId, await db.Set<SaleLine>().Where(l => l.SaleId == sold.Value.SaleId).Select(l => l.Id).SingleAsync());
        }

        PosRefundRequest Refund((long Sale, long Line) sale, long? authoriser, long? tillSession, TenderType? method = null, string account = "") => new()
        {
            CompanyId = companyId, SaleId = sale.Sale, TerminalId = till.Id, CashierUserId = Cashier, AuthorisedByUserId = authoriser, Date = day, Reason = "Faulty", RefundAccountCode = account,
            RefundTender = method, Lines = [new PosRefundLine(sale.Line, 1m)], TillSessionId = tillSession,
        };

        async Task<List<(string Account, decimal Debit, decimal Credit)>> Journal(string number) => [.. (await db.Journals.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == number)).Lines.Select(l => (l.AccountCode, l.BaseDebit, l.BaseCredit))];
        Task<bool> FromDrawer(string journal) => db.TillCashMovements.AnyAsync(m => m.TillSessionId == session && m.JournalNumber == journal);

        // A card sale is refunded back to the card's account by default: no cash leaves the drawer.
        var card = await Sell("RT-CARD", 3m, new PosTenderRequest(TenderType.Card, Scenario.Usd, 30m, "1100", IsChange: false));
        PosRefundResult toCard = (await events.RefundAsync(Refund(card, lead, session, account: "1000"))).Value;
        (await Journal(toCard.JournalNumber)).Should().BeEquivalentTo([("4000", 10m, 0m), ("1100", 0m, 10m)]);
        (await FromDrawer(toCard.JournalNumber)).Should().BeFalse();

        // Cash for a card sale needs the authority to refund by another method.
        (await events.RefundAsync(Refund(card, lead, session, TenderType.Cash, "1000"))).Error.Code.Should().Be("Pos.RefundMethod");
        PosRefundResult inCash = (await events.RefundAsync(Refund(card, manager, session, TenderType.Cash, "1000"))).Value;
        (await Journal(inCash.JournalNumber)).Should().BeEquivalentTo([("4000", 10m, 0m), ("1000", 0m, 10m)]);
        (await FromDrawer(inCash.JournalNumber)).Should().BeTrue("cash paid out comes off what the drawer should hold");

        // Inside the window the authoriser must still hold the refund right: another user is not enough.
        var cash = await Sell("RT-CASH", 3m, new PosTenderRequest(TenderType.Cash, Scenario.Usd, 30m, "1000", IsChange: false));
        (await events.RefundAsync(Refund(cash, clerk, session))).Error.Code.Should().Be("Pos.RefundAuthority");
        (await events.RefundAsync(Refund(cash, 987_654_321, session))).Error.Code.Should().Be("Pos.RefundAuthority");

        // A cash refund comes out of an open session of this till.
        (await events.RefundAsync(Refund(cash, lead, tillSession: null))).Error.Code.Should().Be("Pos.SessionRequired");
        PosRefundResult cashBack = (await events.RefundAsync(Refund(cash, lead, session))).Value;
        (await Journal(cashBack.JournalNumber)).Should().BeEquivalentTo([("4000", 10m, 0m), ("1000", 0m, 10m)], "the sale's own cash account when none is given");
        (await FromDrawer(cashBack.JournalNumber)).Should().BeTrue();

        // An on-account sale is refunded as a credit to the customer, set against the sale's invoice.
        var account = await Sell("RT-ACC", 3m, new PosTenderRequest(TenderType.Credit, Scenario.Usd, 30m, string.Empty, IsChange: false), builder.Id);
        PosRefundResult credited = (await events.RefundAsync(Refund(account, lead, tillSession: null))).Value;
        (await Journal(credited.JournalNumber)).Should().BeEquivalentTo([("4000", 10m, 0m), ("1200", 0m, 10m)]);
        List<OpenItem> items = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == builder.Id).ToListAsync();
        items.Single(i => i.DocumentNumber == credited.JournalNumber).Should().Match<OpenItem>(i => i.ItemType == OpenItemType.CreditNote && i.Sign == LedgerSign.Credit && i.OutstandingBase == 0m);
        items.Single(i => i.DocumentNumber == "RT-ACC").OutstandingBase.Should().Be(20m);
        items.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase).Should().Be(20m, "the customer owes for the two left");
    }
}
