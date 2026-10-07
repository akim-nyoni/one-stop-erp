using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers reading a typed till count and the per-currency variance in base.</summary>
public sealed class CashCountsTests
{
    [Fact]
    public void A_plain_amount_is_base_and_currencies_can_be_listed_either_way_round()
    {
        CashCounts.TryParse("120", "USD", out IReadOnlyList<CashCount> plain).Should().BeTrue();
        plain.Should().Equal(new CashCount("USD", 120m));

        CashCounts.TryParse("120.50; zar 500, 20 EUR", "USD", out IReadOnlyList<CashCount> mixed).Should().BeTrue();
        mixed.Should().Equal(new CashCount("USD", 120.5m), new CashCount("ZAR", 500m), new CashCount("EUR", 20m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("USD 10; USD 5")]
    [InlineData("-5")]
    [InlineData("EURO 5")]
    [InlineData("EUR 5 6")]
    public void Unreadable_or_repeated_counts_are_refused(string text)
        => CashCounts.TryParse(text, "USD", out _).Should().BeFalse();

    [Fact]
    public void The_variance_is_valued_in_base_at_the_rate()
    {
        CurrencyCashUp eur = new("EUR", Expected: 20m, Counted: 15m, Rate: 1.1m);
        (eur.Variance, eur.VarianceBase).Should().Be((-5m, -5.5m));
    }
}
