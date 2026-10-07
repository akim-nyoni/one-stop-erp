using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>
/// Covers promotions on the till ticket: only lines without a manual discount or price override go into the promotion basket; the engine's
/// discounts land on those lines (net and tax follow) and post as the promotion's discount with its account and code; a manual discount
/// later clears the line's promotion.
/// </summary>
public sealed class TicketPromotionTests
{
    [Fact]
    public void Promotion_discounts_price_eligible_lines_and_post_with_their_account()
    {
        PosTicket ticket = new();
        PosTicketLine soda = ticket.AddLine(1, "Soda", 10m, 0.15m, "4000", quantity: 3m);
        PosTicketLine chips = ticket.AddLine(2, "Chips", 5m, 0.15m, "4000");
        PosTicketLine bread = ticket.AddLine(3, "Bread", 4m, 0.15m, "4000");
        ticket.DiscountLine(bread, 10m, 20m, supervised: false).IsSuccess.Should().BeTrue();

        ticket.PromotionBasket().Select(b => (b.ItemId, b.Quantity, b.UnitPrice)).Should().Equal((1L, 3m, 10m), (2L, 1m, 5m));
        decimal saving = ticket.ApplyPromotions(new BasketPricing(
            [new PricedLine(1, 3m, 30m, 10m, ["3FOR2"], "4950"), new PricedLine(2, 1m, 5m, 0m, [], null)], [("3FOR2", "Three for two", 10m)]));

        saving.Should().Be(10m);
        (soda.LineNet, soda.TaxAmount, chips.LineNet).Should().Be((20m, 3m, 5m));
        IReadOnlyList<PosSaleLineRequest> lines = ticket.SaleLines("4960");
        lines[0].Should().Match<PosSaleLineRequest>(l => l.PromotionDiscount == 10m && l.DiscountAccountCode == "4950" && l.PromotionCode == "3FOR2" && l.LineNet == 20m);
        lines[2].Should().Match<PosSaleLineRequest>(l => l.PromotionDiscount == 0.4m && l.DiscountAccountCode == "4960" && l.PromotionCode == "MANUAL");

        // A manual discount on the soda takes it out of promotions.
        ticket.DiscountLine(soda, 5m, 20m, supervised: false).IsSuccess.Should().BeTrue();
        soda.PromotionDiscount.Should().Be(0m);
        ticket.PromotionBasket().Select(b => b.ItemId).Should().Equal(2L);
    }
}
