using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers till cash: cash sales rounded to the smallest coin with the difference posted, and pickups, supervised paid-outs and float
/// top-ups posted and counted in the cash expected at cash-up.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TillCashTests
{
    private readonly AccountingFixture _fixture;

    public TillCashTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cash_is_rounded_and_drawer_movements_reach_the_cash_up()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.CashRoundingIncrement = 0.05m;
        company.CashRoundingAccountCode = "4952";
        db.Accounts.Add(new LedgerAccount(companyId, "4952", "Cash Rounding", AccountType.Income));
        Branch branch = new(companyId, "TC", "Corner Shop");
        db.Branches.Add(branch);
        Item bread = new(companyId, "TC-BREAD", "Bread") { RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "TC-WH", "Shop");
        db.AddRange(bread, shop);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "TCT1", "Till 1");
        db.PosTerminals.Add(terminal);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 100m)).Value;
        Task<Core.Common.Result<CompleteSaleResult>> Sell(string number, params PosTenderRequest[] tenders) => pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = new DateOnly(2026, 7, 1), WarehouseId = shop.Id,
            TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(bread.Id, "Bread", 2m, 4.99m, 9.98m, 0m, "4000")], Tenders = tenders,
        });

        // 9.98 is collected as 10.00 in cash; 10.05 is too far off, and a card is never rounded.
        CashRounding.Round(9.98m, 0.05m).Should().Be(10.00m);
        (await Sell("TC-1", new PosTenderRequest(TenderType.Cash, Scenario.Usd, 10.05m, "1000", false))).Error.Code.Should().Be("Pos.Unbalanced");
        (await Sell("TC-1", new PosTenderRequest(TenderType.Card, Scenario.Usd, 10.00m, "1010", false))).Error.Code.Should().Be("Pos.Unbalanced");
        (await Sell("TC-1", new PosTenderRequest(TenderType.Cash, Scenario.Usd, 10.00m, "1000", false))).IsSuccess.Should().BeTrue();
        (await db.Sales.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == "TC-1")).CashRounding.Should().Be(0.02m);

        // Drawer movements: 50 picked up to the bank, 5 paid out for milk (supervised), 20 float top-up.
        ITillCashService till = scope.ServiceProvider.GetRequiredService<ITillCashService>();
        TillCashRequest Move(TillCashKind kind, decimal amount, string other, long? supervisor = null) => new(session, kind, amount, "1000", other, $"{kind}", 1, supervisor, new DateOnly(2026, 7, 1));
        (await till.RecordAsync(Move(TillCashKind.Pickup, 50m, "1100"))).IsSuccess.Should().BeTrue();
        (await till.RecordAsync(Move(TillCashKind.PaidOut, 5m, "6900"))).Error.Code.Should().Be("Pos.AuthorisationRequired");
        (await till.RecordAsync(Move(TillCashKind.PaidOut, 5m, "6900", supervisor: 1))).Error.Code.Should().Be("Pos.AuthorisationRequired");
        (await till.RecordAsync(Move(TillCashKind.PaidOut, 5m, "6900", supervisor: 2))).IsSuccess.Should().BeTrue();
        (await till.RecordAsync(Move(TillCashKind.FloatTopUp, 20m, "1100"))).IsSuccess.Should().BeTrue();

        // Expected: 100 float + 10 sales − 50 − 5 + 20 = 75; counted 75 balances.
        CashUpResult cashUp = (await pos.CashUpAndCloseAsync(new CashUpRequest
        {
            TillSessionId = session, CountedCash = 75m, Date = new DateOnly(2026, 7, 1), CashAccountCode = "1000", OverShortAccountCode = "6900",
        })).Value;
        cashUp.ExpectedCash.Should().Be(75m);
        cashUp.Variance.Should().Be(0m);
        (await till.RecordAsync(Move(TillCashKind.Pickup, 1m, "1100"))).Error.Code.Should().Be("Pos.SessionClosed");

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 7, 31));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode == "4952").Sum(r => r.Credit - r.Debit).Should().Be(0.02m);
        tb.Rows.Where(r => r.AccountCode == "4000").Sum(r => r.Credit - r.Debit).Should().Be(9.98m);
    }
}
