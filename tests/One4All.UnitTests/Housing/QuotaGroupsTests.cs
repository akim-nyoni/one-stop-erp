using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class QuotaGroupsTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 30);

    [Fact]
    public void Age_and_disability_groups_are_derived_on_the_date()
    {
        QuotaGroups.Of(BeneficiaryGroups.Women, false, new DateOnly(1991, 7, 1), AsAt).Should().Be(BeneficiaryGroups.Women | BeneficiaryGroups.Youth); // still 34
        QuotaGroups.Of(BeneficiaryGroups.None, false, new DateOnly(1990, 6, 30), AsAt).Should().Be(BeneficiaryGroups.None); // turns 36 today
        QuotaGroups.Of(BeneficiaryGroups.None, true, new DateOnly(1966, 6, 30), AsAt).Should().Be(BeneficiaryGroups.Disabled | BeneficiaryGroups.Elderly);
        QuotaGroups.Of(BeneficiaryGroups.CivilServants, false, null, AsAt).Should().Be(BeneficiaryGroups.CivilServants);
    }

    [Fact]
    public void A_share_is_a_percentage_of_allocations()
    {
        QuotaGroups.Share(1, 3).Should().Be(33.3m);
        QuotaGroups.Share(0, 0).Should().Be(0m);
        QuotaGroups.All.Should().HaveCount(6).And.NotContain(BeneficiaryGroups.None);
    }
}
