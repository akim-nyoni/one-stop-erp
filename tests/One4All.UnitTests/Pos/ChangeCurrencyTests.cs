using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Change given in another currency: converted at the rate, rounded to its smallest cash unit, the rest left to rounding.</summary>
public sealed class ChangeCurrencyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");
    private static readonly CurrencyCode Zwg = CurrencyCode.Parse("ZWG");

    [Theory]
    [InlineData(10.02, 251, 10.04, -0.02)]   // 250.5 rounds up to 251
    [InlineData(10.01, 250, 10.00, 0.01)]    // 250.25 rounds down to 250
    [InlineData(0.01, 0, 0.00, 0.01)]        // less than half a unit: nothing handed over
    public void Change_is_rounded_to_the_smallest_cash_unit(decimal owed, decimal amount, decimal baseValue, decimal rounding)
    {
        PosChangeQuote quote = PosChangeQuote.For(owed, Zwg, 0.04m, 1m);
        (quote.Amount, quote.BaseValue, quote.Rounding).Should().Be((amount, baseValue, rounding));
    }

    [Fact]
    public void A_ticket_gives_its_change_in_the_quoted_currency()
    {
        PosTicket ticket = new();
        ticket.AddLine(0, "Bread", 9.98m, 0m, "4000");
        ticket.AddTender(TenderType.Cash, 20m).IsSuccess.Should().BeTrue();
        PosChangeQuote quote = PosChangeQuote.For(ticket.ChangeDue, Zwg, 0.04m, 1m);
        CompleteSaleRequest sale = ticket.BuildSale(new PosSaleContext(1, 1, 1, 1, "S1", new DateOnly(2026, 7, 1), 1, Usd, null, "1000",
            new Dictionary<TenderType, string> { [TenderType.Cash] = "1000" }, Change: quote)).Value;
        sale.Tenders.Should().Equal(new PosTenderRequest(TenderType.Cash, Usd, 20m, "1000", IsChange: false),
            new PosTenderRequest(TenderType.Cash, Zwg, 251m, "1000", IsChange: true, ExchangeRate: 0.04m));
    }
}
