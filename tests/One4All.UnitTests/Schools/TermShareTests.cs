using FluentAssertions;
using One4All.Core.Schools;
using Xunit;

namespace One4All.UnitTests.Schools;

/// <summary>Covers the pro-rata share of a term: whole term, joiners, leavers, both, outside the term, and pro rata switched off.</summary>
public sealed class TermShareTests
{
    private static FeeStructure Term(bool proRata)
    {
        FeeStructure structure = new(1, "T", "Term", "G1", "Term 1", "4200");
        structure.SetTerm(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), proRata);
        return structure;
    }

    [Theory]
    [InlineData(null, null, 1)]
    [InlineData("2025-12-01", null, 1)]
    [InlineData("2026-02-15", null, 0.5)]
    [InlineData(null, "2026-03-01", 0.6667)]
    [InlineData("2026-02-15", "2026-03-01", 0.1667)]
    [InlineData("2026-04-02", null, 0)]
    public void The_share_counts_the_days_enrolled_in_the_term(string? from, string? to, double expected)
        => Term(proRata: true).ShareOfTerm(from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)).Should().Be((decimal)expected);

    [Fact]
    public void Without_pro_rata_everyone_pays_the_whole_term()
        => Term(proRata: false).ShareOfTerm(new DateOnly(2026, 3, 1), null).Should().Be(1m);
}
