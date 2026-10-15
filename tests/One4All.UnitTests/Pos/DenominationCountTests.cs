using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers counting cash note by note at a cash-up.</summary>
public sealed class DenominationCountTests
{
    [Fact]
    public void Notes_and_coins_add_up_and_loose_amounts_count_as_they_are()
    {
        DenominationCount.TryRead("50x10 + 20 x 5 + 0.5×4 + 3.25", out decimal total, out IReadOnlyList<DenominationLine> lines).Should().BeTrue();
        total.Should().Be(605.25m);
        lines.Should().Equal(new DenominationLine(50m, 10), new DenominationLine(20m, 5), new DenominationLine(0.5m, 4), new DenominationLine(3.25m, 1));
        DenominationCount.Describe(lines).Should().Be("10 × 50 = 500.00; 5 × 20 = 100.00; 4 × 0.5 = 2.00; 3.25");
    }

    [Theory]
    [InlineData("")]
    [InlineData("50x")]
    [InlineData("50x2.5")]
    [InlineData("0x3")]
    [InlineData("50x-1")]
    [InlineData("50x2x3")]
    [InlineData("fifty")]
    public void A_count_that_cannot_be_read_is_refused(string text) => DenominationCount.TryRead(text, out _, out _).Should().BeFalse();

    [Fact]
    public void Each_note_must_be_one_of_the_currency_s_own()
    {
        decimal[] usd = [100m, 50m, 20m, 10m, 5m, 1m];
        DenominationCount.UnknownFace("50x2 + 20x1 + 7.30", usd).Should().BeNull();
        DenominationCount.UnknownFace("50x2 + 25x1", usd).Should().Be(25m);
        DenominationCount.UnknownFace("25x1", []).Should().BeNull();
    }

    [Fact]
    public void A_till_count_can_be_typed_note_by_note_in_each_currency()
    {
        CashCounts.TryParse("50 x 10 + 20x5; ZAR 100x3", "USD", out IReadOnlyList<CashCount> counts).Should().BeTrue();
        counts.Should().Equal(new CashCount("USD", 600m), new CashCount("ZAR", 300m));
        CashCounts.TryParse("120 XAF", "USD", out IReadOnlyList<CashCount> franc).Should().BeTrue();
        franc.Should().Equal(new CashCount("XAF", 120m));
    }
}
