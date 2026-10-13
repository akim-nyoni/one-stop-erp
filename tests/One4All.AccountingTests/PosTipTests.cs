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
/// Covers tips at the till: a sale of 23 with a tip of 2 is tendered 30 with 5 change; the tip is credited to the tips account (not revenue),
/// kept on the sale and shown on the X-read; a tip without an account and tenders that do not cover the tip are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosTipTests
{
    private readonly AccountingFixture _fixture;

    public PosTipTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_tip_is_paid_with_the_sale_and_owed_to_staff()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "TIP-HO", "Head Office");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "TIP-TILL", "Bar till");
        Item item = new(companyId, "TIP-SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse store = new(companyId, "TIP-WH", "Bar");
        db.AddRange(terminal, item, store);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 50m)).Value;
        CompleteSaleRequest Sale(string number, decimal tip, string? tipsAccount, decimal cash, decimal change) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = new DateOnly(2026, 3, 2),
            WarehouseId = store.Id, TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(item.Id, "Soda", 10m, 2m, 20m, 3m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, cash, "1000", IsChange: false), new PosTenderRequest(TenderType.Cash, Scenario.Usd, change, "1000", IsChange: true)],
            Tip = tip, TipsAccountCode = tipsAccount,
        };

        (await pos.CompleteSaleAsync(Sale("TIP-0", 2m, null, 30m, 5m))).Error.Code.Should().Be("Pos.Tip");
        (await pos.CompleteSaleAsync(Sale("TIP-0", 2m, "2400", 30m, 7m))).Error.Code.Should().Be("Pos.Unbalanced");
        CompleteSaleResult sold = (await pos.CompleteSaleAsync(Sale("TIP-1", 2m, "2400", 30m, 5m))).Value;
        sold.TotalGross.Should().Be(23m);

        (await db.Set<Sale>().AsNoTracking().SingleAsync(s => s.Id == sold.SaleId)).Tip.Should().Be(2m);
        List<JournalLine> lines = await (from l in db.Set<JournalLine>().AsNoTracking()
                                         join j in db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                         where j.CompanyId == companyId && j.JournalNumber == sold.JournalNumber
                                         select l).ToListAsync();
        lines.Single(l => l.AccountCode == "2400").BaseCredit.Should().Be(2m);
        lines.Where(l => l.AccountCode == "4000").Sum(l => l.BaseCredit).Should().Be(20m);

        TillRead read = (await pos.XReadAsync(session)).Value;
        read.Tips.Should().Be(2m);
        read.TotalGross.Should().Be(23m);
    }
}
