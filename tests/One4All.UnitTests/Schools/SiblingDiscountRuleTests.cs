using FluentAssertions;
using One4All.Core.Schools;
using Xunit;

namespace One4All.UnitTests.Schools;

/// <summary>Covers choosing the sibling rule for a child and stacking it after a bursary.</summary>
public sealed class SiblingDiscountRuleTests
{
    [Fact]
    public void Child_takes_the_highest_rule_not_above_its_place()
    {
        SiblingDiscount[] rules = [new(1, 2, 10m), new(1, 3, 20m)];
        SiblingDiscount.PercentFor(rules, 1).Should().Be(0m);
        SiblingDiscount.PercentFor(rules, 2).Should().Be(10m);
        SiblingDiscount.PercentFor(rules, 7).Should().Be(20m);
    }

    [Fact]
    public void Sibling_discount_applies_after_the_bursary()
    {
        Student child = new(1, "S1", "Child", "G1", 9);
        child.GrantBursary(BursaryType.Percentage, 50m);
        child.ComputeDiscounts(1_000m, 10m).Should().Be((500m, 50m));
    }
}
