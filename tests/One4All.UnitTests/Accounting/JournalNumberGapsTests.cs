using FluentAssertions;
using One4All.Application.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers <see cref="JournalNumberGaps"/>: gap runs per year, the leading gap, and ignored formats.</summary>
public class JournalNumberGapsTests
{
    [Fact]
    public void Contiguous_numbering_has_no_gaps()
    {
        JournalNumberGaps.Find(["JNL-2026-000001", "JNL-2026-000002", "JNL-2026-000003"]).Should().BeEmpty();
    }

    [Fact]
    public void Gaps_are_reported_as_runs_per_year_including_a_missing_start()
    {
        IReadOnlyList<NumberGap> gaps = JournalNumberGaps.Find(
        [
            "JNL-2026-000001", "JNL-2026-000002", "JNL-2026-000006", "JNL-2026-000008",
            "JNL-2027-000003", "JNL-2027-000004",
        ]);

        gaps.Should().Equal(new NumberGap(2026, 3, 5), new NumberGap(2026, 7, 7), new NumberGap(2027, 1, 2));
        JournalNumberGaps.Describe(gaps[0]).Should().Be("JNL-2026-000003 to JNL-2026-000005");
        JournalNumberGaps.Describe(gaps[1]).Should().Be("JNL-2026-000007");
        gaps[0].Count.Should().Be(3);
    }

    [Fact]
    public void Other_formats_and_duplicates_are_ignored()
    {
        JournalNumberGaps.Find(["", "MANUAL-7", "JNL-26-000001", "JNL-2026-ABC", "JNL-2026-000001", "JNL-2026-000001", "JNL-2026-000002"])
            .Should().BeEmpty();
    }
}
