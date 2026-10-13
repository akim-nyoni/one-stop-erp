using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers deposit items at the till: a crate deposit sold with the drinks posts to the deposits-held liability and issues no stock; a deposit
/// with tax, or posted to a revenue account, is refused; fuel is stock-tracked.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosDepositTests
{
    private readonly AccountingFixture _fixture;

    public PosDepositTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deposits_are_held_as_liabilities()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2313", "Container Deposits Held", AccountType.Liability));
        Branch branch = new(companyId, "DEP-HO", "Head Office");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "DEP-TILL", "Bottle store");
        Item crate = new(companyId, "DEP-CRATE", "Crate deposit", ItemType.Deposit) { RevenueAccountCode = "2313" };
        Item soda = new(companyId, "DEP-SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Item diesel = new(companyId, "DEP-DIESEL", "Diesel", ItemType.Fuel);
        Warehouse store = new(companyId, "DEP-WH", "Store");
        db.AddRange(terminal, crate, soda, diesel, store);
        await db.SaveChangesAsync();
        diesel.IsStockTracked.Should().BeTrue();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        CompleteSaleRequest Sale(string number, decimal depositTax, string depositAccount) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = new DateOnly(2026, 3, 2), WarehouseId = store.Id,
            TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(soda.Id, "Soda", 1m, 20m, 20m, 0m, "4000"), new PosSaleLineRequest(crate.Id, "Crate deposit", 1m, 5m, 5m, depositTax, depositAccount)],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 25m + depositTax, "1000", IsChange: false)],
        };

        (await pos.CompleteSaleAsync(Sale("DEP-0", 0.75m, "2313"))).Error.Code.Should().Be("Pos.DepositLine");
        (await pos.CompleteSaleAsync(Sale("DEP-0", 0m, "4000"))).Error.Code.Should().Be("Pos.DepositLine");
        CompleteSaleResult sold = (await pos.CompleteSaleAsync(Sale("DEP-1", 0m, "2313"))).Value;

        List<JournalLine> lines = await (from l in db.Set<JournalLine>().AsNoTracking()
                                         join j in db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                         where j.CompanyId == companyId && j.JournalNumber == sold.JournalNumber
                                         select l).ToListAsync();
        lines.Single(l => l.AccountCode == "2313").BaseCredit.Should().Be(5m);
        lines.Where(l => l.AccountCode == "4000").Sum(l => l.BaseCredit).Should().Be(20m);
    }
}
