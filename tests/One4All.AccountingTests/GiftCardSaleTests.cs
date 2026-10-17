using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers selling and topping up gift cards at the till (brief §4.8.1): a ticket line on the GIFTCARD item loads the card through the till session
/// (the tender is cash, card or mobile money; the value is a liability, not revenue), printed stock activates, an active card tops up, the card then
/// pays for goods, and a void takes the load back unless the card has been spent.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class GiftCardSaleTests
{
    private readonly AccountingFixture _fixture;

    public GiftCardSaleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Gift_cards_are_sold_and_topped_up_at_the_till()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "GC", "Gift Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "GCT1", "Gift Till");
        Item giftCard = new(companyId, "GIFTCARD", "Gift card", ItemType.Service) { RevenueAccountCode = "2300" };
        Item tea = new(companyId, "GC-TEA", "Tea") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "GCW", "Shop");
        db.AddRange(terminal, giftCard, tea, wh);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IGiftCardServicing>().CreateRangeAsync(companyId, "GCS", 1, 3, 4, Scenario.Usd, new DateOnly(2026, 3, 1))).Value.Should().Be(3);
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        DateOnly day = new(2026, 3, 2);
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 5, openingFloat: 50m)).Value;
        CompleteSaleRequest Sale(string number, PosSaleLineRequest[] lines, PosTenderRequest[] tenders) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 5, SaleNumber = number, SaleDate = day, WarehouseId = wh.Id, TaxAccountCode = "2200", Lines = lines, Tenders = tenders,
        };
        PosTenderRequest Cash(decimal amount) => new(TenderType.Cash, Scenario.Usd, amount, "1000", IsChange: false);
        PosSaleLineRequest Load(string card, decimal value, decimal tax = 0m) => new(giftCard.Id, $"Gift card {card}", 1m, value, value, tax, "2300", GiftCardNumber: card);

        (await pos.CompleteSaleAsync(Sale("GC-0", [Load("GCS0001", 30m, tax: 4.5m)], [Cash(34.5m)]))).Error.Code.Should().Be("Pos.GiftCardLine");
        (await pos.CompleteSaleAsync(Sale("GC-0", [Load("NOPE", 30m)], [Cash(30m)]))).Error.Code.Should().Be("Voucher.NotFound");

        // Printed stock activates at its sale; the value is a liability.
        var sold = await pos.CompleteSaleAsync(Sale("GC-1", [Load("gcs0001", 30m)], [Cash(30m)]));
        sold.IsSuccess.Should().BeTrue(sold.IsFailure ? sold.Error.Message : null);
        GiftCard card = await db.Set<GiftCard>().AsNoTracking().SingleAsync(g => g.CompanyId == companyId && g.CardNumber == "GCS0001");
        (card.Status, card.Balance, card.OriginalValue, card.IssuedDate).Should().Be((GiftCardStatus.Active, 30m, 30m, day));
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2300").SumAsync(l => l.BaseCredit - l.BaseDebit)).Should().Be(30m);
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "4000").SumAsync(l => l.BaseCredit - l.BaseDebit)).Should().Be(0m, "a card is not revenue");

        // An active card tops up, with goods on the same ticket; then it pays for goods.
        var topped = await pos.CompleteSaleAsync(Sale("GC-2", [Load("GCS0001", 20m), new PosSaleLineRequest(tea.Id, "Tea", 1m, 4m, 4m, 0m, "4000")], [Cash(24m)]));
        topped.IsSuccess.Should().BeTrue(topped.IsFailure ? topped.Error.Message : null);
        (await db.Set<GiftCard>().AsNoTracking().SingleAsync(g => g.Id == card.Id)).Balance.Should().Be(50m);
        (await db.Set<GiftCardTransaction>().AsNoTracking().Where(t => t.GiftCardId == card.Id).OrderBy(t => t.Id).Select(t => t.Event).ToListAsync()).Should().Equal(GiftCardEvent.Activated, GiftCardEvent.ToppedUp);
        var paid = await pos.CompleteSaleAsync(Sale("GC-3", [new PosSaleLineRequest(tea.Id, "Tea", 1m, 4m, 4m, 0m, "4000")], [new PosTenderRequest(TenderType.Voucher, Scenario.Usd, 4m, "2300", IsChange: false, VoucherNumber: "GCS0001")]));
        paid.IsSuccess.Should().BeTrue(paid.IsFailure ? paid.Error.Message : null);
        (await db.Set<GiftCard>().AsNoTracking().SingleAsync(g => g.Id == card.Id)).Balance.Should().Be(46m);

        // Voiding the top-up takes its 20 back; voiding the original sale cannot, as the card has spent below 30.
        (await pos.VoidSaleAsync(companyId, topped.Value.SaleId, day, "Wrong card", "2200")).IsSuccess.Should().BeTrue();
        (await db.Set<GiftCard>().AsNoTracking().SingleAsync(g => g.Id == card.Id)).Balance.Should().Be(26m);
        (await pos.VoidSaleAsync(companyId, sold.Value.SaleId, day, "Too late", "2200")).Error.Code.Should().Be("Pos.GiftCardSpent");
    }
}
