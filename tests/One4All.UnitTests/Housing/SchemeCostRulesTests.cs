using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class SchemeCostRulesTests
{
    [Fact]
    public void Shares_add_back_exactly_with_the_cents_on_the_last()
    {
        SchemeCostRules.Spread(1000.01m, 3).Should().Equal(333.33m, 333.33m, 333.35m);
        SchemeCostRules.Spread(300m, 2).Should().Equal(150m, 150m);
        FluentActions.Invoking(() => SchemeCostRules.Spread(1m, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_budget_compares_as_amount_left_and_share_used()
    {
        SchemeCostRules.Compare(10000m, 9000m).Should().Be(new BudgetComparison(1000m, 90.0m));
        SchemeCostRules.Compare(0m, 300m).Should().Be(new BudgetComparison(-300m, null));
    }
}
