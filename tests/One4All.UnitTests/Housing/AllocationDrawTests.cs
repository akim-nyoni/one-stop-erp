using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class AllocationDrawTests
{
    private static readonly string[] Six = ["A", "B", "C", "D", "E", "F"];

    [Fact]
    public void The_ballot_matches_an_independently_computed_draw()
    {
        // Expected orders computed outside .NET from the published SplitMix64 + Fisher–Yates definition.
        AllocationDraw.Ballot(Six, 42).Should().Equal("E", "D", "A", "C", "F", "B");
        AllocationDraw.Ballot(Six, 7).Should().Equal("B", "F", "A", "C", "E", "D");
    }

    [Fact]
    public void The_same_seed_always_gives_the_same_draw_and_every_candidate_once()
    {
        IReadOnlyList<string> first = AllocationDraw.Ballot(Six, 123456789);
        AllocationDraw.Ballot(Six, 123456789).Should().Equal(first);
        first.Should().BeEquivalentTo(Six);
    }

    [Fact]
    public void The_input_hash_is_sha256_of_the_ordered_inputs()
    {
        AllocationDraw.InputHash(["A", "B"], ["S1"]).Should().Be("5c14e0a2b05103dd9b297cb7ceb8f83a4236758971a6a1fc3ed3574b9ab4cd7d");
        AllocationDraw.InputHash(["B", "A"], ["S1"]).Should().NotBe(AllocationDraw.InputHash(["A", "B"], ["S1"]));
    }

    [Fact]
    public void Pairing_stops_when_stands_or_candidates_run_out()
    {
        AllocationDraw.Pair(["S1", "S2"], ["A", "B", "C"]).Should().Equal(("S1", "A"), ("S2", "B"));
        AllocationDraw.Pair(["S1", "S2", "S3"], ["A"]).Should().Equal(("S1", "A"));
    }

    [Fact]
    public void An_empty_or_single_list_is_unchanged()
    {
        AllocationDraw.Ballot([], 1).Should().BeEmpty();
        AllocationDraw.Ballot(["A"], 1).Should().Equal("A");
    }
}
