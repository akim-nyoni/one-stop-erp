using FluentAssertions;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers <see cref="DeferredIncomeSchedule.EarnedBy"/>: income earned evenly by day over the service period.</summary>
public sealed class DeferredIncomeScheduleTests
{
    // A 90-day term: 1 January to 31 March 2026.
    private static DeferredIncomeSchedule Term(decimal amount) =>
        new(1, "INV-1", "Term 1 fees", "2480", "4200", amount, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    [Fact]
    public void Nothing_is_earned_before_the_term()
    {
        Term(900m).EarnedBy(new DateOnly(2025, 12, 31)).Should().Be(0m);
    }

    [Fact]
    public void Income_is_earned_by_the_day()
    {
        Term(900m).EarnedBy(new DateOnly(2026, 1, 1)).Should().Be(10m);   // 1 of 90 days
        Term(900m).EarnedBy(new DateOnly(2026, 1, 31)).Should().Be(310m); // 31 of 90 days
        Term(1_000m).EarnedBy(new DateOnly(2026, 1, 31)).Should().Be(344.44m);
    }

    [Fact]
    public void Everything_is_earned_from_the_last_day()
    {
        Term(1_000m).EarnedBy(new DateOnly(2026, 3, 31)).Should().Be(1_000m);
        Term(1_000m).EarnedBy(new DateOnly(2026, 6, 30)).Should().Be(1_000m);
    }

    [Fact]
    public void Invalid_schedules_are_refused()
    {
        FluentActions.Invoking(() => Term(0m)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new DeferredIncomeSchedule(1, "X", "X", "2480", "4200", 10m, new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 1)))
            .Should().Throw<ArgumentException>();
    }
}
