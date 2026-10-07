using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>
/// Covers splitting a basket: by items (whole lines and part quantities, with net, tax and discount following the quantity and the last
/// share taking the rounding), evenly, and the splits that are refused.
/// </summary>
public sealed class SplitBillTests
{
    private static readonly PosSaleLineRequest Pizza = new(1, "Pizza", 3m, 10m, 30m, 4.5m, "4000", 1m, "6900", "PROMO");
    private static readonly PosSaleLineRequest Wine = new(2, "Wine", 1m, 20m, 20m, 3m, "4000");
    private static readonly PosSaleLineRequest[] Basket = [Pizza, Wine];

    [Fact]
    public void Items_split_with_amounts_following_quantity()
    {
        var plan = SplitBill.Plan(Basket, [[new SplitShare(0, 1m), new SplitShare(1, 1m)], [new SplitShare(0, 2m)]]);
        plan.IsSuccess.Should().BeTrue();
        plan.Value[0].Should().Equal(Pizza with { Quantity = 1m, LineNet = 10m, TaxAmount = 1.5m, PromotionDiscount = 0.33m }, Wine);
        plan.Value[1].Should().Equal(Pizza with { Quantity = 2m, LineNet = 20m, TaxAmount = 3m, PromotionDiscount = 0.67m });
        (SplitBill.Gross(plan.Value[0]) + SplitBill.Gross(plan.Value[1])).Should().Be(SplitBill.Gross(Basket));
    }

    [Fact]
    public void An_even_split_adds_back_exactly()
    {
        PosSaleLineRequest odd = new(3, "Platter", 1m, 10m, 10m, 1m, "4000");
        IReadOnlyList<IReadOnlyList<SplitShare>> shares = SplitBill.Evenly([odd], 3);
        shares.Select(p => p.Single().Quantity).Should().Equal(0.3333m, 0.3333m, 0.3334m);
        var plan = SplitBill.Plan([odd], shares);
        plan.Value.Select(p => p.Single().LineNet).Should().Equal(3.33m, 3.33m, 3.34m);
        plan.Value.Select(p => p.Single().TaxAmount).Should().Equal(0.33m, 0.33m, 0.34m);
        plan.Value.Sum(SplitBill.Gross).Should().Be(11m);
    }

    [Fact]
    public void Bad_splits_are_refused()
    {
        SplitBill.Plan(Basket, [[new SplitShare(0, 3m), new SplitShare(1, 1m)]]).Error.Code.Should().Be("Pos.SplitParts");
        SplitBill.Plan(Basket, [[new SplitShare(0, 3m), new SplitShare(1, 1m)], []]).Error.Code.Should().Be("Pos.SplitEmptyPart");
        SplitBill.Plan(Basket, [[new SplitShare(0, 2m)], [new SplitShare(1, 1m)]]).Error.Code.Should().Be("Pos.SplitUnbalanced");
        SplitBill.Plan(Basket, [[new SplitShare(0, 3m)], [new SplitShare(1, 1m), new SplitShare(2, 1m)]]).Error.Code.Should().Be("Pos.SplitShare");
        SplitBill.Plan(Basket, [[new SplitShare(0, 4m), new SplitShare(1, 1m)], [new SplitShare(0, -1m)]]).Error.Code.Should().Be("Pos.SplitShare");
    }

    [Fact]
    public void A_ticket_voucher_tender_carries_its_card()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0m, "4000");
        ticket.AddTender(Core.Pos.TenderType.Voucher, 5m).Error.Code.Should().Be("Pos.VoucherTender");
        ticket.AddTender(Core.Pos.TenderType.Voucher, 15m, " gc-9 ").IsSuccess.Should().BeTrue();
        ticket.AddTender(Core.Pos.TenderType.Cash, 5m).IsSuccess.Should().BeTrue();
        CompleteSaleRequest request = ticket.BuildSale(new PosSaleContext(1, 1, 1, 1, "S1", new DateOnly(2026, 6, 1), 1, Core.Accounting.CurrencyCode.Parse("USD"), null, "1000",
            new Dictionary<Core.Pos.TenderType, string> { [Core.Pos.TenderType.Voucher] = "2300", [Core.Pos.TenderType.Cash] = "1000" })).Value;
        request.Tenders.Should().Contain(t => t.TenderType == Core.Pos.TenderType.Voucher && t.VoucherNumber == "GC-9" && t.AccountCode == "2300" && t.Amount == 15m);
    }

    [Fact]
    public void The_basket_needs_no_tenders_and_splits_like_the_sale()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0.15m, "4000", quantity: 2m);
        PosSaleContext context = new(1, 1, 1, 1, "S2", new DateOnly(2026, 6, 1), 1, Core.Accounting.CurrencyCode.Parse("USD"), "2200", "1000", new Dictionary<Core.Pos.TenderType, string>());
        ticket.BuildSale(context).Error.Code.Should().Be("Pos.Underpaid");
        CompleteSaleRequest basket = ticket.BuildBasket(context).Value;
        basket.Tenders.Should().BeEmpty();
        SplitBill.Plan(basket.Lines, SplitBill.Evenly(basket.Lines, 2)).Value.Select(SplitBill.Gross).Should().Equal(23m, 23m);
        new PosTicket().BuildBasket(context).Error.Code.Should().Be("Pos.Empty");
    }

    [Fact]
    public void Foreign_tenders_convert_at_their_rate_and_change_is_exact_in_base()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0.15m, "4000");
        Core.Accounting.CurrencyCode eur = Core.Accounting.CurrencyCode.Parse("EUR"), usd = Core.Accounting.CurrencyCode.Parse("USD");
        ticket.BalanceDueIn(1.10m).Should().Be(20.91m);
        ticket.AddForeignTender(Core.Pos.TenderType.Voucher, eur, 1.10m, 5m).Error.Code.Should().Be("Pos.ForeignTender");
        ticket.AddForeignTender(Core.Pos.TenderType.Cash, eur, 1.10m, 20.91m).IsSuccess.Should().BeTrue();
        (ticket.Tendered, ticket.ChangeDue, ticket.IsFullyTendered).Should().Be((23.001m, 0.001m, true));
        CompleteSaleRequest sale = ticket.BuildSale(new PosSaleContext(1, 1, 1, 1, "S3", new DateOnly(2026, 6, 1), 1, usd, "2200", "1000",
            new Dictionary<Core.Pos.TenderType, string> { [Core.Pos.TenderType.Cash] = "1000" })).Value;
        sale.Tenders.Should().Equal(new PosTenderRequest(Core.Pos.TenderType.Cash, eur, 20.91m, "1000", IsChange: false, ExchangeRate: 1.10m),
            new PosTenderRequest(Core.Pos.TenderType.Cash, usd, 0.001m, "1000", IsChange: true));
    }

    [Fact]
    public void An_account_tender_needs_the_customer()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0m, "4000");
        ticket.AddTender(Core.Pos.TenderType.Credit, 20m).IsSuccess.Should().BeTrue();
        PosSaleContext context = new(1, 1, 1, 1, "S4", new DateOnly(2026, 6, 1), 1, Core.Accounting.CurrencyCode.Parse("USD"), null, "1000", new Dictionary<Core.Pos.TenderType, string>());
        ticket.BuildSale(context).Error.Code.Should().Be("Pos.AccountCustomer");
        CompleteSaleRequest sale = ticket.BuildSale(context with { CustomerId = 77 }).Value;
        (sale.CustomerId, sale.Tenders.Single().TenderType).Should().Be((77L, Core.Pos.TenderType.Credit));
    }
}
