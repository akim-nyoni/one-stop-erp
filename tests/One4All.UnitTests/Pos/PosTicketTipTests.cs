using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers a tip on the till ticket: it adds to what is due, the change is worked out on the sale plus the tip, and the sale needs a tips account.</summary>
public sealed class PosTicketTipTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");

    [Fact]
    public void A_tip_is_added_to_what_the_customer_pays()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Soda", 10.00m, 0m, "4000", quantity: 2m);
        ticket.SetTip(-1m).IsFailure.Should().BeTrue();
        ticket.SetTip(3m).IsSuccess.Should().BeTrue();
        ticket.AmountDue.Should().Be(23m);
        ticket.AddTender(TenderType.Cash, 25m);
        ticket.ChangeDue.Should().Be(2m);

        Dictionary<TenderType, string> accounts = new() { [TenderType.Cash] = "1000" };
        PosSaleContext context = new(1, 1, 1, 1, "S-1", new DateOnly(2026, 3, 2), 1, Usd, null, "1000", accounts);
        ticket.BuildSale(context).Error.Code.Should().Be("Pos.TipsAccount");
        CompleteSaleRequest sale = ticket.BuildSale(context with { TipsAccountCode = "2400" }).Value;
        sale.Tip.Should().Be(3m);
        sale.TipsAccountCode.Should().Be("2400");
        sale.Tenders.Sum(t => t.IsChange ? -t.Amount : t.Amount).Should().Be(23m);

        ticket.Clear();
        ticket.Tip.Should().Be(0m);
    }
}
