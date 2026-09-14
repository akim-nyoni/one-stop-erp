using FluentAssertions;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" eur ", "EUR")]
    [InlineData("ZWL", "ZWL")]
    public void Parse_normalizes_valid_codes(string input, string expected)
    {
        CurrencyCode.Parse(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U5D")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_rejects_invalid_codes(string input)
    {
        Action act = () => CurrencyCode.Parse(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParse_returns_false_for_invalid()
    {
        CurrencyCode.TryParse("bad!", out _).Should().BeFalse();
        CurrencyCode.TryParse("gbp", out CurrencyCode code).Should().BeTrue();
        code.Value.Should().Be("GBP");
    }

    [Fact]
    public void Default_value_is_empty()
    {
        CurrencyCode code = default;
        code.IsEmpty.Should().BeTrue();
        code.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Equality_is_ordinal()
    {
        CurrencyCode.Parse("USD").Should().Be(CurrencyCode.Parse("usd"));
        (CurrencyCode.Parse("USD") == CurrencyCode.Parse("EUR")).Should().BeFalse();
    }
}
