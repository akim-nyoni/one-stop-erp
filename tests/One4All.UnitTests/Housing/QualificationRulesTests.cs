using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

/// <summary>Covers the configurable qualification rules beyond age, residency and income.</summary>
public sealed class QualificationRulesTests
{
    [Fact]
    public void Each_rule_applies_only_when_switched_on()
    {
        QualificationFacts household = new(HousingCategory.HighDensity, 900m, EverHeldProperty: true, Forfeited: true, OverdueCharges: 40m);
        WaitingListRules.Problems(QualificationRules.None, household).Should().BeEmpty();

        QualificationRules strict = new(true, true, true, new Dictionary<HousingCategory, decimal> { [HousingCategory.HighDensity] = 800m, [HousingCategory.LowDensity] = 5000m });
        IReadOnlyList<string> problems = WaitingListRules.Problems(strict, household);
        problems.Should().HaveCount(4);
        problems[0].Should().Contain("800.00 limit for HighDensity");
        problems[3].Should().Contain("Owes 40.00");

        WaitingListRules.Problems(strict, household with { Category = HousingCategory.Flats, EverHeldProperty = false, Forfeited = false, OverdueCharges = 0m }).Should().BeEmpty("no ceiling for flats");
    }
}
