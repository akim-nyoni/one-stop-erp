using FluentAssertions;
using One4All.Application.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers budget utilisation percentages and the alert threshold reached.</summary>
public sealed class BudgetUtilisationTests
{
    [Theory]
    [InlineData(1_000, 800, 80)]
    [InlineData(300, 100, 33.33)]
    [InlineData(0, 50, 100)]
    [InlineData(0, 0, 0)]
    public void Utilisation_is_the_share_of_the_appropriation_used(int appropriation, int used, double percent)
    {
        BudgetUtilisation.PercentUsed(appropriation, used).Should().Be((decimal)percent);
    }

    [Theory]
    [InlineData(74.99, null)]
    [InlineData(75.0, 75.0)]
    [InlineData(92.0, 90.0)]
    [InlineData(125.0, 100.0)]
    public void The_highest_threshold_reached_is_reported(double percent, double? crossed)
    {
        BudgetUtilisation.HighestCrossed((decimal)percent, BudgetUtilisation.DefaultThresholds).Should().Be((decimal?)crossed);
    }
}
