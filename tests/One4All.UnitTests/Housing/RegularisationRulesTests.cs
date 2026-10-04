using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class RegularisationRulesTests
{
    private static readonly DateOnly CutOff = new(2025, 12, 31);
    private static readonly DateOnly Enumerated = new(2026, 3, 1);

    [Fact]
    public void An_adult_occupying_before_the_cut_off_qualifies()
    {
        RegularisationRules.Problems(new HouseholdFacts(new DateOnly(1985, 5, 1), new DateOnly(2019, 1, 1), Enumerated, false), CutOff, 18).Should().BeEmpty();
    }

    [Fact]
    public void Occupation_on_the_cut_off_date_still_qualifies()
    {
        RegularisationRules.Problems(new HouseholdFacts(new DateOnly(1985, 5, 1), CutOff, Enumerated, false), CutOff, 18).Should().BeEmpty();
    }

    [Fact]
    public void Every_failed_rule_is_reported()
    {
        IReadOnlyList<string> problems = RegularisationRules.Problems(new HouseholdFacts(new DateOnly(2010, 5, 1), new DateOnly(2026, 1, 15), Enumerated, true), CutOff, 18);
        problems.Should().HaveCount(3);
        problems[0].Should().Contain("after the cut-off date 2025-12-31");
        problems[1].Should().Contain("is 15, under the minimum age of 18");
        problems[2].Should().Contain("already holds council land or property");
    }

    [Fact]
    public void Age_is_taken_on_the_enumeration_date()
    {
        HouseholdFacts turnsEighteenAfter = new(new DateOnly(2008, 3, 2), new DateOnly(2020, 1, 1), Enumerated, false);
        RegularisationRules.Problems(turnsEighteenAfter, CutOff, 18).Should().ContainSingle().Which.Should().Contain("is 17");
        RegularisationRules.Problems(turnsEighteenAfter with { EnumeratedOn = new DateOnly(2026, 3, 2) }, CutOff, 18).Should().BeEmpty();
    }
}
