using FluentAssertions;
using One4All.Application.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers the bank auto-match rule: amount, date window, reference preference, closest date, single use.</summary>
public sealed class BankAutoMatcherTests
{
    [Fact]
    public void Same_amount_outside_the_date_window_does_not_match()
    {
        MatchableStatementLine[] statement = [new(1, new DateOnly(2026, 6, 10), 100m, null)];
        MatchableGlLine[] gl = [new(50, new DateOnly(2026, 1, 10), 100m, null, null)];

        BankAutoMatcher.Match(statement, gl, 7).Should().BeEmpty();
        BankAutoMatcher.Match(statement, gl, -1).Should().ContainSingle(); // unlimited window
    }

    [Fact]
    public void Closest_date_wins_and_each_gl_line_is_used_once()
    {
        MatchableStatementLine[] statement =
        [
            new(1, new DateOnly(2026, 3, 10), 100m, null),
            new(2, new DateOnly(2026, 3, 14), 100m, null),
        ];
        MatchableGlLine[] gl =
        [
            new(50, new DateOnly(2026, 3, 13), 100m, null, null),
            new(51, new DateOnly(2026, 3, 9), 100m, null, null),
        ];

        IReadOnlyList<BankMatch> matches = BankAutoMatcher.Match(statement, gl, 7);

        matches.Should().HaveCount(2);
        matches.Single(m => m.StatementLineId == 1).GlLineId.Should().Be(51); // 1 day away beats 3
        matches.Single(m => m.StatementLineId == 2).GlLineId.Should().Be(50); // the only one left
    }

    [Fact]
    public void Reference_match_beats_a_closer_date_and_amount_sign_must_agree()
    {
        MatchableStatementLine[] statement = [new(1, new DateOnly(2026, 3, 10), -250m, "chq 1001")];
        MatchableGlLine[] gl =
        [
            new(60, new DateOnly(2026, 3, 10), -250m, "CHQ 1002", null),
            new(61, new DateOnly(2026, 3, 15), -250m, "CHQ 1001", null),
            new(62, new DateOnly(2026, 3, 10), 250m, "CHQ 1001", null), // wrong sign
        ];

        IReadOnlyList<BankMatch> matches = BankAutoMatcher.Match(statement, gl, 7);

        BankMatch match = matches.Should().ContainSingle().Subject;
        match.GlLineId.Should().Be(61);
        match.ReferenceMatched.Should().BeTrue();
        match.DaysApart.Should().Be(5);
    }
}
