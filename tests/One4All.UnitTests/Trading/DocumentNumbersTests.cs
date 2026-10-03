using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers document-number comparison: punctuation, spacing and case are ignored; empty never matches.</summary>
public sealed class DocumentNumbersTests
{
    [Theory]
    [InlineData("INV-0042", "inv 0042", true)]
    [InlineData("INV-0042", "INV0042", true)]
    [InlineData("INV/0042.", "Inv_0042", true)]
    [InlineData("INV-0042", "INV-0043", false)]
    [InlineData("INV-042", "INV-0042", false)]
    [InlineData("--", "  ", false)]
    [InlineData(null, "", false)]
    public void Same_ignores_punctuation_spacing_and_case(string? a, string? b, bool expected) =>
        DocumentNumbers.Same(a, b).Should().Be(expected);

    [Fact]
    public void Key_keeps_letters_and_digits_upper_cased()
    {
        DocumentNumbers.Key("ab-12 c").Should().Be("AB12C");
        DocumentNumbers.Key(null).Should().BeEmpty();
    }
}
