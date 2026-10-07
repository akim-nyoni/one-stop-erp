using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers per-currency and blind cash-up: a till taking dollars and euros expects each currency separately (change given in dollars on
/// a euro tender); a blind till refuses to show the expected cash or close until the count is declared, and the declared count cannot
/// be changed; the variance is posted per currency at the session's rate (euros short, dollars over) with the net to over/short.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BlindCashUpTests
{
    private readonly AccountingFixture _fixture;

    public BlindCashUpTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_blind_till_is_counted_per_currency_before_the_expected_cash_is_seen()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "BL", "Blind Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "BLIND1", "Blind Till") { BlindCashUp = true };
        Item item = new(companyId, "BL-SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "BLW", "Shop");
        db.PosTerminals.Add(terminal);
        db.Items.Add(item);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        DateOnly day = new(2026, 3, 2);
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 5, openingFloat: 50m)).Value;
        // A dollar sale: 30 in, 3 change. A euro sale of 22 (USD) paid with EUR 20 at 1.10, no change.
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 5, SaleNumber = "BL-1", SaleDate = day, WarehouseId = wh.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(item.Id, "Soda", 1m, 27m, 27m, 0m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 30m, "1000", IsChange: false), new PosTenderRequest(TenderType.Cash, Scenario.Usd, 3m, "1000", IsChange: true)],
        })).IsSuccess.Should().BeTrue();
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 5, SaleNumber = "BL-2", SaleDate = day, WarehouseId = wh.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(item.Id, "Soda", 1m, 22m, 22m, 0m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Eur, 20m, "1000", IsChange: false)],
        })).IsSuccess.Should().BeTrue();

        // Blind: nothing expected is shown, and the till cannot be closed, until the count is declared.
        (await pos.ExpectedCashAsync(session)).Error.Code.Should().Be("Pos.BlindCountMissing");
        (await pos.CloseTillAsync(session)).Error.Code.Should().Be("Pos.BlindCountMissing");
        CashUpRequest close = new() { TillSessionId = session, Date = day, CashAccountCode = "1000", OverShortAccountCode = "7500", CountedByUserId = 5 };
        (await pos.CashUpAndCloseAsync(close)).Error.Code.Should().Be("Pos.BlindCountMissing");

        // Counted: USD 79 (1 over the 77 expected), EUR 15 (5 short).
        (await pos.DeclareCountAsync(session, [new CashCount("usd", 79m), new CashCount("EUR", 15m)], 5)).IsSuccess.Should().BeTrue();
        (await pos.DeclareCountAsync(session, [new CashCount("USD", 80m)], 5)).Error.Code.Should().Be("Pos.CountDeclared");
        IReadOnlyList<CurrencyCashUp> position = (await pos.ExpectedCashAsync(session)).Value;
        position.Select(p => (p.Currency, p.Expected, p.Counted)).Should().Equal(("EUR", 20m, 15m), ("USD", 77m, 79m));
        (await pos.CashUpAndCloseAsync(new CashUpRequest { TillSessionId = session, Date = day, CashAccountCode = "1000", OverShortAccountCode = "7500", CountedCash = 99m }))
            .Error.Code.Should().Be("Pos.CountDeclared");

        CashUpResult result = (await pos.CashUpAndCloseAsync(close)).Value;
        result.ByCurrency.Select(p => (p.Currency, p.Variance, p.VarianceBase)).Should().Equal(("EUR", -5m, -5.5m), ("USD", 2m, 2m));
        (result.ExpectedCash, result.CountedCash, result.Variance).Should().Be((99m, 95.5m, -3.5m));

        // The journal: USD cash Dr 2, EUR cash Cr 5.50, over/short Dr 3.50.
        JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.DocumentNumber == $"CASHUP-{session}");
        journal.Lines.Sum(l => l.BaseDebit).Should().Be(5.5m);
        journal.Lines.Where(l => l.Narrative != null && l.Narrative.StartsWith("EUR")).Single().BaseCredit.Should().Be(5.5m);
        (await db.Set<TillCashCount>().CountAsync(c => c.TillSessionId == session && c.Blind)).Should().Be(2);
    }
}
