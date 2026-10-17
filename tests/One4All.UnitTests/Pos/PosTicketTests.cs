using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>
/// Covers the touch-terminal cart/tender engine: line and ticket maths, tendering with change, and
/// building a balanced <see cref="CompleteSaleRequest"/> whose net tender equals the gross total (the
/// invariant the POS service enforces).
/// </summary>
public sealed class PosTicketTests
{
    private static PosSaleContext Context(params (TenderType, string)[] tenderAccounts) => new(
        CompanyId: 1, TerminalId: 2, TillSessionId: 3, CashierUserId: 4,
        SaleNumber: "R-001", SaleDate: new DateOnly(2026, 9, 28), WarehouseId: 5,
        Currency: CurrencyCode.Parse("USD"), TaxAccountCode: "2200", ChangeAccountCode: "1000",
        TenderAccounts: tenderAccounts.ToDictionary(t => t.Item1, t => t.Item2));

    [Fact]
    public void Line_maths_add_tax_on_top_of_net()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Soda", unitPriceNet: 2.00m, taxRate: 0.15m, revenueAccountCode: "4000", quantity: 10m);

        PosTicketLine line = ticket.Lines.Single();
        line.LineNet.Should().Be(20.00m);
        line.TaxAmount.Should().Be(3.00m);
        line.LineGross.Should().Be(23.00m);

        ticket.SubtotalNet.Should().Be(20.00m);
        ticket.TaxTotal.Should().Be(3.00m);
        ticket.GrossTotal.Should().Be(23.00m);
    }

    [Fact]
    public void Value_discounts_apply_to_a_line_and_spread_across_a_sale()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Soda", 2.00m, 0m, "4000", quantity: 10m);
        ticket.AddLine(1, "Bread", 5.00m, 0m, "4000", quantity: 2m);
        PosTicketLine soda = ticket.Lines[0];
        ticket.DiscountLineAmount(soda, 25m, 10m, supervised: false).Error.Code.Should().Be("Pos.Discount", "more than the line is worth");
        ticket.DiscountLineAmount(soda, 5m, 10m, supervised: false).Error.Code.Should().Be("Pos.SupervisorRequired", "5 off 20 is 25%, above the 10% limit");
        ticket.DiscountLineAmount(soda, 5m, 10m, supervised: true).IsSuccess.Should().BeTrue();
        (soda.Discount, soda.LineNet).Should().Be((5m, 15m));

        ticket.DiscountSaleAmount(3.3m, 10m, supervised: false).Error.Code.Should().Be("Pos.SupervisorRequired", "3.30 off 30 is 11%");
        ticket.DiscountSaleAmount(3m, 10m, supervised: false).IsSuccess.Should().BeTrue();
        ticket.Lines.Select(l => l.Discount).Should().Equal(new[] { 2.00m, 1.00m }, "spread in proportion to the lines' value, replacing the line discount");
        ticket.SubtotalNet.Should().Be(27m);
        ticket.DiscountSaleAmount(0.01m, 10m, supervised: false).IsSuccess.Should().BeTrue();
        ticket.Lines.Select(l => l.Discount).Should().Equal(new[] { 0.01m, 0m }, "the last line takes the rounding, here nothing");
        ticket.DiscountSaleAmount(0m, 10m, supervised: false).IsSuccess.Should().BeTrue();
        ticket.SubtotalNet.Should().Be(30m);
    }

    [Fact]
    public void Adding_the_same_sellable_increments_quantity()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Coffee", 3.00m, 0m, "4000");
        ticket.AddLine(0, "Coffee", 3.00m, 0m, "4000");

        ticket.Lines.Should().HaveCount(1);
        ticket.Lines.Single().Quantity.Should().Be(2m);
        ticket.GrossTotal.Should().Be(6.00m);
    }

    [Fact]
    public void Balance_and_change_track_tenders()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0m, "4000");

        ticket.BalanceDue.Should().Be(20.00m);
        ticket.IsFullyTendered.Should().BeFalse();

        ticket.AddTender(TenderType.Card, 5.00m);
        ticket.BalanceDue.Should().Be(15.00m);
        ticket.ChangeDue.Should().Be(0m);
        ticket.IsFullyTendered.Should().BeFalse();

        ticket.AddTender(TenderType.Cash, 20.00m);
        ticket.IsFullyTendered.Should().BeTrue();
        ticket.BalanceDue.Should().Be(0m);
        ticket.ChangeDue.Should().Be(5.00m);
    }

    [Fact]
    public void AddTender_rejects_a_non_positive_amount()
    {
        PosTicket ticket = new();
        ticket.AddTender(TenderType.Cash, 0m).IsFailure.Should().BeTrue();
        ticket.AddTender(TenderType.Cash, -1m).IsFailure.Should().BeTrue();
        ticket.Tenders.Should().BeEmpty();
    }

    [Fact]
    public void BuildSale_produces_a_request_whose_net_tender_equals_the_gross()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Soda", 2.00m, 0.15m, "4000", quantity: 10m); // gross 23.00
        ticket.AddTender(TenderType.Cash, 25.00m); // change 2.00

        Result<CompleteSaleRequest> result = ticket.BuildSale(Context((TenderType.Cash, "1000")));

        result.IsSuccess.Should().BeTrue();
        CompleteSaleRequest sale = result.Value;

        sale.Lines.Should().ContainSingle();
        sale.Lines[0].LineNet.Should().Be(20.00m);
        sale.Lines[0].TaxAmount.Should().Be(3.00m);

        // One cash tender (25.00) and one change tender (2.00, IsChange) → net tendered 23.00 == gross.
        sale.Tenders.Should().HaveCount(2);
        decimal netTendered = sale.Tenders.Sum(t => t.IsChange ? -t.Amount : t.Amount);
        netTendered.Should().Be(ticket.GrossTotal);

        PosTenderRequest change = sale.Tenders.Single(t => t.IsChange);
        change.Amount.Should().Be(2.00m);
        change.AccountCode.Should().Be("1000");
    }

    [Fact]
    public void BuildSale_maps_each_tender_to_its_configured_account()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0m, "4000");
        ticket.AddTender(TenderType.Card, 12.00m);
        ticket.AddTender(TenderType.Cash, 8.00m);

        Result<CompleteSaleRequest> result = ticket.BuildSale(Context((TenderType.Cash, "1000"), (TenderType.Card, "1050")));

        result.IsSuccess.Should().BeTrue();
        result.Value.Tenders.Single(t => t.TenderType == TenderType.Card).AccountCode.Should().Be("1050");
        result.Value.Tenders.Single(t => t.TenderType == TenderType.Cash && !t.IsChange).AccountCode.Should().Be("1000");
    }

    [Fact]
    public void BuildSale_fails_when_underpaid()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0m, "4000");
        ticket.AddTender(TenderType.Cash, 5.00m);

        Result<CompleteSaleRequest> result = ticket.BuildSale(Context((TenderType.Cash, "1000")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pos.Underpaid");
    }

    [Fact]
    public void BuildSale_fails_when_a_tender_account_is_missing()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Meal", 20.00m, 0m, "4000");
        ticket.AddTender(TenderType.Card, 20.00m);

        Result<CompleteSaleRequest> result = ticket.BuildSale(Context((TenderType.Cash, "1000")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pos.TenderAccount");
    }

    [Fact]
    public void BuildSale_fails_on_an_empty_ticket()
    {
        PosTicket ticket = new();
        ticket.BuildSale(Context((TenderType.Cash, "1000"))).Error.Code.Should().Be("Pos.Empty");
    }
}
