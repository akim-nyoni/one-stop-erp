using FluentAssertions;
using One4All.Application.Banking;
using One4All.Core.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers recurring due dates.</summary>
public sealed class RecurrenceTests
{
    [Fact]
    public void Month_end_starts_keep_to_month_ends()
        => Recurrence.Due(new DateOnly(2026, 1, 31), null, RecurrenceFrequency.Monthly, null, new DateOnly(2026, 4, 30))
            .Should().Equal(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30));

    [Fact]
    public void Runs_continue_after_the_last_and_stop_at_the_end()
    {
        Recurrence.Due(new DateOnly(2026, 1, 15), new DateOnly(2026, 6, 30), RecurrenceFrequency.Quarterly, new DateOnly(2026, 1, 15), new DateOnly(2026, 12, 31))
            .Should().Equal(new DateOnly(2026, 4, 15));
        Recurrence.Due(new DateOnly(2026, 1, 1), null, RecurrenceFrequency.Weekly, null, new DateOnly(2026, 1, 20)).Should().HaveCount(3);
        Recurrence.Occurrence(new DateOnly(2024, 2, 29), RecurrenceFrequency.Annually, 1).Should().Be(new DateOnly(2025, 2, 28));
    }
}
