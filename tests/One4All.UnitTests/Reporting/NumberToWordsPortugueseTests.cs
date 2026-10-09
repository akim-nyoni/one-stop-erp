using FluentAssertions;
using One4All.Reporting.Expressions;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers numbers and amounts in Portuguese words for cheques and vouchers.</summary>
public sealed class NumberToWordsPortugueseTests
{
    [Theory]
    [InlineData(0, "zero")]
    [InlineData(1, "um")]
    [InlineData(16, "dezasseis")]
    [InlineData(21, "vinte e um")]
    [InlineData(100, "cem")]
    [InlineData(101, "cento e um")]
    [InlineData(115, "cento e quinze")]
    [InlineData(200, "duzentos")]
    [InlineData(999, "novecentos e noventa e nove")]
    [InlineData(1000, "mil")]
    [InlineData(1001, "mil e um")]
    [InlineData(1100, "mil e cem")]
    [InlineData(1150, "mil cento e cinquenta")]
    [InlineData(1200, "mil e duzentos")]
    [InlineData(2345, "dois mil trezentos e quarenta e cinco")]
    [InlineData(1_000_000, "um milhão")]
    [InlineData(2_500_000, "dois milhões e quinhentos mil")]
    [InlineData(3_000_000_000, "três mil milhões")]
    public void Numbers_read_as_they_are_written_on_a_cheque(long value, string words) => NumberToWordsPortuguese.Convert(value).Should().Be(words);

    [Fact]
    public void Amounts_take_their_units_in_the_singular_or_plural()
    {
        NumberToWordsPortuguese.Amount(115.05m, "metical", "meticais", "centavo", "centavos").Should().Be("Cento e quinze meticais e cinco centavos");
        NumberToWordsPortuguese.Amount(1.01m, "metical", "meticais", "centavo", "centavos").Should().Be("Um metical e um centavo");
        NumberToWordsPortuguese.Amount(2_000_000m, "metical", "meticais", "centavo", "centavos").Should().Be("Dois milhões de meticais");
        NumberToWordsPortuguese.Amount(1_234.50m, "dólar", "dólares", "cêntimo", "cêntimos").Should().Be("Mil duzentos e trinta e quatro dólares e cinquenta cêntimos");
    }
}
