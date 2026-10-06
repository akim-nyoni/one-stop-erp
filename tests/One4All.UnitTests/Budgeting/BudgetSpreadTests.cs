using FluentAssertions;
using One4All.Application.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers spreading an annual figure over periods.</summary>
public sealed class BudgetSpreadTests
{
    [Fact]
    public void Weights_split_to_the_cent_and_add_back_to_the_annual()
    {
        BudgetSpread.ByWeights(100m, [1m, 1m, 1m]).Should().Equal(33.33m, 33.33m, 33.34m);
        BudgetSpread.ByWeights(1_000m, [0m, 0m]).Should().Equal(500m, 500m);
        BudgetSpread.ByWeights(900m, [1m, 2m]).Should().Equal(300m, 600m);
    }

    [Fact]
    public void The_s_curve_is_slow_fast_slow_and_working_days_skip_weekends()
    {
        IReadOnlyList<decimal> curve = BudgetSpread.SCurve(12);
        curve[0].Should().BeLessThan(curve[5]);
        curve[11].Should().BeLessThan(curve[6]);
        BudgetSpread.WorkingDays([(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))]).Should().Equal(20m);
    }
}
