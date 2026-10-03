using FluentAssertions;
using One4All.Core.System;
using Xunit;

namespace One4All.UnitTests.Common;

/// <summary>Covers <see cref="NumberSequence.Allocate"/>: continuous numbering and per-year counters.</summary>
public class NumberSequenceTests
{
    [Fact]
    public void Continuous_sequence_ignores_the_year()
    {
        NumberSequence sequence = new(1, "PO", "PO-", 5);

        sequence.Allocate(2026).Should().Be("PO-00001");
        sequence.Allocate(2027).Should().Be("PO-00002");
        sequence.Allocate(2026).Should().Be("PO-00003");
    }

    [Fact]
    public void Yearly_sequence_keeps_a_counter_per_year_when_years_alternate()
    {
        NumberSequence sequence = new(1, "GLJournal", "JNL-", 6, resetYearly: true);

        sequence.Allocate(2026).Should().Be("JNL-2026-000001");
        sequence.Allocate(2026).Should().Be("JNL-2026-000002");
        sequence.Allocate(2027).Should().Be("JNL-2027-000001");

        // Back-dating into the earlier open year continues its numbering instead of restarting at 1.
        sequence.Allocate(2026).Should().Be("JNL-2026-000003");
        sequence.Allocate(2027).Should().Be("JNL-2027-000002");

        sequence.Years.Should().HaveCount(2);
        sequence.CurrentYear.Should().Be(2027);
        sequence.NextValue.Should().Be(3);
    }
}
