using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the till's live tile search: every typed word must appear in the code, name, barcode or PLU, in any case.</summary>
public sealed class PosTileTests
{
    private static readonly PosTile Bread = new(1, "BR-700", "White bread 700g", "Bakery", 1.2m, "4000", "VAT", "6001234500017", "4011");

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("bread", true)]
    [InlineData("WHITE 700", true)]
    [InlineData("br-7", true)]
    [InlineData("600123", true)]
    [InlineData("4011", true)]
    [InlineData("brown bread", false)]
    public void Search_matches_every_word(string? text, bool expected) => Bread.Matches(text).Should().Be(expected);
}
