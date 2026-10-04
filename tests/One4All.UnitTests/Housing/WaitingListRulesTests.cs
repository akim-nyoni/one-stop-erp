using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class WaitingListRulesTests
{
    private static readonly WaitingListTerms Terms = new(18, 5, 2000m, 10m, 2m, 5m, 15m, 20m, 8m);
    private static readonly DateOnly AsAt = new(2026, 6, 1);

    private static ApplicantFacts Facts(int bornYear = 1990, int residentYear = 2010, int registeredYear = 2020, int dependants = 2, bool disability = false, decimal income = 1500m, CurrentTenure tenure = CurrentTenure.Lodger)
        => new(new DateOnly(bornYear, 3, 1), new DateOnly(residentYear, 1, 1), new DateOnly(registeredYear, 7, 1), dependants, disability, income, tenure);

    [Fact]
    public void Full_years_count_only_completed_anniversaries()
    {
        WaitingListRules.FullYears(new DateOnly(2020, 7, 1), new DateOnly(2026, 6, 30)).Should().Be(5);
        WaitingListRules.FullYears(new DateOnly(2020, 7, 1), new DateOnly(2026, 7, 1)).Should().Be(6);
        WaitingListRules.FullYears(new DateOnly(2026, 7, 1), new DateOnly(2020, 7, 1)).Should().Be(0);
    }

    [Fact]
    public void A_qualifying_applicant_has_no_problems()
    {
        WaitingListRules.Problems(Facts(), Terms, ownsProperty: false, previouslyAllocated: false, AsAt).Should().BeEmpty();
    }

    [Fact]
    public void Every_failed_rule_is_reported()
    {
        IReadOnlyList<string> problems = WaitingListRules.Problems(Facts(bornYear: 2010, residentYear: 2024, income: 2500m), Terms, ownsProperty: true, previouslyAllocated: true, AsAt);
        problems.Should().HaveCount(5);
        problems[0].Should().Contain("minimum age of 18");
        problems[1].Should().Contain("Resident 2 years; 5 required");
    }

    [Fact]
    public void The_score_adds_each_factor_and_explains_it()
    {
        // 5 years on the list (from July 2020 to June 2026), 16 years resident, 2 dependants, no disability, lodger.
        PriorityScore score = WaitingListRules.Score(Facts(), Terms, AsAt);
        score.Lines.Select(l => l.Points).Should().Equal(50m, 32m, 10m, 0m, 8m);
        score.Total.Should().Be(100m);
        score.Lines[0].Detail.Should().Be("5 × 10");
    }

    [Theory]
    [InlineData(CurrentTenure.Homeless, 20)]
    [InlineData(CurrentTenure.InformalSettlement, 20)]
    [InlineData(CurrentTenure.WithRelatives, 8)]
    [InlineData(CurrentTenure.Tenant, 0)]
    public void Housing_conditions_earn_points(CurrentTenure tenure, int points)
    {
        WaitingListRules.Score(Facts(tenure: tenure), Terms, AsAt).Lines.Single(l => l.Factor == "Housing conditions").Points.Should().Be(points);
    }

    [Fact]
    public void Disability_earns_its_points()
    {
        WaitingListRules.Score(Facts(disability: true), Terms, AsAt).Total.Should().Be(115m);
    }
}
