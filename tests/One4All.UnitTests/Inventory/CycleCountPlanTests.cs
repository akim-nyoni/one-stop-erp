using FluentAssertions;
using One4All.Application.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

public sealed class CycleCountPlanTests
{
    private static readonly DateOnly Today = new(2026, 6, 30);

    [Fact]
    public void An_item_is_due_once_its_class_interval_has_passed()
    {
        CycleCountCandidate[] items =
        [
            new(1, "A", Today.AddDays(-30)), // A every 30 days: due today
            new(2, "A", Today.AddDays(-29)), // not yet
            new(3, "B", Today.AddDays(-60)), // B every 90: not yet
            new(4, "C", Today.AddDays(-200)), // C every 180: 20 days overdue
            new(5, null, Today.AddDays(-179)), // unclassified counts as C: not yet
        ];

        CycleCountPlan.Due(items, new CycleCountRule(), Today).Select(d => d.ItemId).Should().Equal(1, 4);
    }

    [Fact]
    public void Never_counted_items_come_first_then_A_before_B_before_C_then_the_most_overdue()
    {
        CycleCountCandidate[] items =
        [
            new(1, "C", Today.AddDays(-400)),
            new(2, "B", Today.AddDays(-100)),
            new(3, "A", Today.AddDays(-31)),
            new(4, "A", Today.AddDays(-60)),
            new(5, "C", null),
        ];

        CycleCountPlan.Due(items, new CycleCountRule(), Today).Select(d => d.ItemId).Should().Equal(5, 4, 3, 2, 1);
    }

    [Fact]
    public void A_count_takes_at_most_the_limit_keeping_the_most_urgent()
    {
        CycleCountCandidate[] items = [.. Enumerable.Range(1, 10).Select(i => new CycleCountCandidate(i, "B", Today.AddDays(-90 - i)))];

        CycleCountPlan.Due(items, new CycleCountRule(MaxItems: 3), Today).Select(d => d.ItemId).Should().Equal(10, 9, 8);
    }

    [Fact]
    public void The_intervals_are_the_rules()
    {
        CycleCountRule rule = new(ADays: 7, BDays: 14, CDays: 28);
        rule.IntervalFor("a").Should().Be(7);
        rule.IntervalFor("B").Should().Be(14);
        rule.IntervalFor(null).Should().Be(28);
    }
}
