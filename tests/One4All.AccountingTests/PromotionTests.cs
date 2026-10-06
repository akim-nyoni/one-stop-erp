using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers POS promotions end to end: configured promotions run by happy-hour window, membership and coupon; a basket is priced with each
/// line's discount; the sale posts revenue gross and the discount to the promotion's account, and a refund reverses both shares.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PromotionTests
{
    private readonly AccountingFixture _fixture;

    public PromotionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Promotions_price_the_basket_and_post_their_discount()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4951", "Promotional Discounts", AccountType.Expense));
        Branch branch = new(companyId, "PR", "Promo Store");
        db.Branches.Add(branch);
        Item cola = new(companyId, "PR-COLA", "Cola 330ml") { RevenueAccountCode = "4000", Category = "Drinks" };
        Item juice = new(companyId, "PR-JUICE", "Juice 1l") { RevenueAccountCode = "4000", Category = "Drinks" };
        Item crisps = new(companyId, "PR-CRISP", "Crisps") { RevenueAccountCode = "4000", Category = "Snacks" };
        Warehouse shop = new(companyId, "PR-WH", "Shop");
        db.AddRange(cola, juice, crisps, shop);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "PRT1", "Till 1");
        db.PosTerminals.Add(terminal);
        await db.SaveChangesAsync();

        IPromotionService promotions = scope.ServiceProvider.GetRequiredService<IPromotionService>();
        PromotionRequest Promo(string code, PromotionKind kind) => new() { CompanyId = companyId, Code = code, Name = code, Kind = kind, DiscountAccountCode = "4951", StartsOn = new DateOnly(2026, 1, 1) };
        (await promotions.SaveAsync(Promo("BAD", PromotionKind.NForPrice) with { Quantity = 1 })).Error.Code.Should().Be("Pos.PromotionInvalid");
        (await promotions.SaveAsync(Promo("3FOR2", PromotionKind.BuyXGetY) with { Quantity = 2, RewardQuantity = 1, Percent = 100m, Items = ["PR-COLA", "PR-JUICE"], Priority = 1 })).IsSuccess.Should().BeTrue();
        (await promotions.SaveAsync(Promo("HAPPY", PromotionKind.PercentOff) with
        {
            Percent = 20m, Category = "Snacks", DailyFrom = new TimeOnly(16, 0), DailyTo = new TimeOnly(18, 0), Priority = 2,
        })).IsSuccess.Should().BeTrue();
        (await promotions.SaveAsync(Promo("MEMBER5", PromotionKind.PercentOff) with { Percent = 5m, Category = "Snacks", MemberOnly = true, Compounding = true, Priority = 3 })).IsSuccess.Should().BeTrue();
        (await promotions.SaveAsync(Promo("COUPON", PromotionKind.PercentOff) with { Percent = 50m, Items = ["PR-JUICE"], CouponCode = "half", Priority = 0 })).IsSuccess.Should().BeTrue();

        BasketRequest Basket(int hour, bool member = false, params string[] coupons) => new(companyId, branch.Id, new DateTime(2026, 6, 1, hour, 30, 0), member, coupons,
            [new BasketItem(cola.Id, 2m, 1.50m), new BasketItem(juice.Id, 1m, 3m), new BasketItem(crisps.Id, 2m, 2m)]);

        // Noon: three drinks for the price of two (the cheapest, a cola, free); crisps full price.
        BasketPricing noon = await promotions.PriceBasketAsync(Basket(12));
        noon.Lines.Select(l => l.Discount).Should().Equal(1.5m, 0m, 0m);

        // Happy hour, as a member: crisps 20% off then 5% more on what is left.
        BasketPricing happy = await promotions.PriceBasketAsync(Basket(17, member: true));
        happy.Lines[2].Discount.Should().Be(0.8m + 0.16m);
        happy.Applied.Select(a => a.Code).Should().BeEquivalentTo(["3FOR2", "HAPPY", "MEMBER5"]);

        // A coupon at higher priority halves the juice first, so the 3-for-2 no longer has three drinks free to group.
        BasketPricing coupon = await promotions.PriceBasketAsync(Basket(12, coupons: "HALF"));
        coupon.Lines.Select(l => l.Discount).Should().Equal(0m, 1.5m, 0m);

        // The happy-hour sale: revenue at gross, the 2.46 of discounts to 4951.
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        List<PosSaleLineRequest> lines = [.. happy.Lines.Select(l => new PosSaleLineRequest(l.ItemId, "Line", l.Quantity, l.Gross / l.Quantity, l.Net, 0m, "4000", l.Discount,
            l.Discount > 0m ? l.DiscountAccountCode : null, string.Join(',', l.PromotionCodes)))];
        decimal total = happy.Lines.Sum(l => l.Net);
        var sale = await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = "PR-1", SaleDate = new DateOnly(2026, 6, 1), WarehouseId = shop.Id,
            TaxAccountCode = "2200", Lines = lines, Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, total, "1000", false)],
        });
        sale.IsSuccess.Should().BeTrue(sale.IsFailure ? sale.Error.ToString() : null);
        IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 6, 30));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode == "4000").Sum(r => r.Credit - r.Debit).Should().Be(10m);
        tb.Rows.Where(r => r.AccountCode == "4951").Sum(r => r.Debit - r.Credit).Should().Be(2.46m);

        // Refunding one bag of crisps returns its share of both.
        long crispsLine = (await db.Sales.AsNoTracking().Include(s => s.Lines).SingleAsync(s => s.Id == sale.Value.SaleId)).Lines.Single(l => l.ItemId == crisps.Id).Id;
        (await scope.ServiceProvider.GetRequiredService<IPosEventService>().RefundAsync(new PosRefundRequest
        {
            CompanyId = companyId, SaleId = sale.Value.SaleId, TerminalId = terminal.Id, CashierUserId = 1, AuthorisedByUserId = 2, Date = new DateOnly(2026, 6, 2), RefundAccountCode = "1000",
            Reason = "Damaged", Lines = [new PosRefundLine(crispsLine, 1m)],
        })).IsSuccess.Should().BeTrue();
        tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 6, 30));
        tb.Rows.Where(r => r.AccountCode == "4000").Sum(r => r.Credit - r.Debit).Should().Be(8m);
        tb.Rows.Where(r => r.AccountCode == "4951").Sum(r => r.Debit - r.Credit).Should().Be(1.98m);
    }
}
