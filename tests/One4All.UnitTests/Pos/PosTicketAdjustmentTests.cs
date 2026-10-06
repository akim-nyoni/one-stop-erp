using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>
/// Covers the till's adjustments: line and whole-sale discounts (a percentage follows the quantity; above the cashier's limit a supervisor
/// is needed), price overrides (always supervised), discounts carried to the sale as a discount line on their own account, and a parked
/// ticket loading back with its prices, discounts and quantities.
/// </summary>
public sealed class PosTicketAdjustmentTests
{
    private static PosSaleContext Context(string? discountAccount) => new(
        CompanyId: 1, TerminalId: 2, TillSessionId: 3, CashierUserId: 4, SaleNumber: "R-9", SaleDate: new DateOnly(2026, 10, 6), WarehouseId: 5,
        Currency: CurrencyCode.Parse("USD"), TaxAccountCode: "2200", ChangeAccountCode: "1000", TenderAccounts: new Dictionary<TenderType, string> { [TenderType.Cash] = "1000" },
        DiscountAccountCode: discountAccount);

    [Fact]
    public void Discounts_follow_the_quantity_and_need_a_supervisor_past_the_limit()
    {
        PosTicket ticket = new();
        PosTicketLine bread = ticket.AddLine(0, "Bread", 2.00m, 0.15m, "4000", quantity: 5m);

        ticket.DiscountLine(bread, 25m, unsupervisedLimitPercent: 10m, supervised: false).Error.Code.Should().Be("Pos.SupervisorRequired");
        ticket.DiscountLine(bread, 10m, 10m, supervised: false).IsSuccess.Should().BeTrue();
        (bread.LineNetBeforeDiscount, bread.Discount, bread.LineNet, bread.TaxAmount).Should().Be((10.00m, 1.00m, 9.00m, 1.35m));
        bread.Quantity = 2m;
        (bread.Discount, bread.LineNet).Should().Be((0.40m, 3.60m));
        ticket.DiscountLine(bread, 101m, 10m, true).Error.Code.Should().Be("Pos.Discount");
        ticket.DiscountLine(bread, 0m, 10m, false).IsSuccess.Should().BeTrue();
        bread.Discount.Should().Be(0m);

        PosTicketLine milk = ticket.AddLine(0, "Milk", 1.00m, 0m, "4000", quantity: 3m);
        ticket.DiscountSale(20m, 10m, supervised: true).IsSuccess.Should().BeTrue();
        (bread.Discount, milk.Discount).Should().Be((0.80m, 0.60m));
        ticket.SubtotalNet.Should().Be(3.20m + 2.40m);
    }

    [Fact]
    public void Price_overrides_are_supervised_and_discounts_post_to_their_account()
    {
        PosTicket ticket = new();
        PosTicketLine tv = ticket.AddLine(10, "Television", 400m, 0m, "4000");
        ticket.OverridePrice(tv, 350m, supervised: false).Error.Code.Should().Be("Pos.SupervisorRequired");
        ticket.OverridePrice(tv, 350m, supervised: true).IsSuccess.Should().BeTrue();
        (tv.IsOverridden, tv.ListUnitPriceNet, tv.LineNet).Should().Be((true, 400m, 350m));

        ticket.DiscountLine(tv, 10m, 5m, supervised: true).IsSuccess.Should().BeTrue();
        ticket.AddTender(TenderType.Cash, 315m).IsSuccess.Should().BeTrue();
        ticket.BuildSale(Context(null)).Error.Code.Should().Be("Pos.DiscountAccount");
        CompleteSaleRequest sale = ticket.BuildSale(Context("4090")).Value;
        sale.Lines.Should().ContainSingle().Which.Should().Match<PosSaleLineRequest>(l => l.UnitPrice == 350m && l.LineNet == 315m && l.PromotionDiscount == 35m && l.DiscountAccountCode == "4090");
        sale.Tenders.Sum(t => t.IsChange ? -t.Amount : t.Amount).Should().Be(315m);
    }

    [Fact]
    public void A_parked_ticket_comes_back_as_it_was()
    {
        PosTicket ticket = new();
        PosTicketLine soap = ticket.AddLine(3, "Soap", 1.50m, 0.15m, "4000", quantity: 4m);
        ticket.DiscountLine(soap, 10m, 10m, false);
        IReadOnlyList<PosSaleLineRequest> parked = ticket.SaleLines("4090");
        decimal total = ticket.GrossTotal;

        PosTicket resumed = new();
        resumed.AddLine(9, "Something else", 1m, 0m, "4000");
        resumed.Load(parked, _ => 0.15m).Error.Code.Should().Be("Pos.NotEmpty");
        resumed.Clear();
        resumed.Load(parked, _ => 0.15m).IsSuccess.Should().BeTrue();
        resumed.GrossTotal.Should().Be(total);
        resumed.Lines.Single().Should().Match<PosTicketLine>(l => l.Quantity == 4m && l.Discount == 0.60m);
    }
}
