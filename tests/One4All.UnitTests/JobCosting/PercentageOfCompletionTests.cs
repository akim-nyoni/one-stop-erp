using FluentAssertions;
using One4All.Application.JobCosting;
using Xunit;

namespace One4All.UnitTests.JobCosting;

/// <summary>Covers the cost-to-cost percentage-of-completion maths.</summary>
public sealed class PercentageOfCompletionTests
{
    [Fact]
    public void Under_billed_contract_carries_a_contract_asset()
    {
        // 25% complete on 1,000,000 → earned 250,000; certified 200,000 → asset 50,000; nothing posted before.
        PocCalculation poc = PercentageOfCompletion.Compute(1_000_000m, 800_000m, 200_000m, 200_000m, 0m);

        poc.PercentComplete.Should().Be(25m);
        poc.EarnedRevenue.Should().Be(250_000m);
        poc.CumulativeAdjustment.Should().Be(50_000m);
        poc.Movement.Should().Be(50_000m);
        poc.IsContractAsset.Should().BeTrue();
        poc.IsContractLiability.Should().BeFalse();
    }

    [Fact]
    public void Over_billed_contract_carries_a_contract_liability_and_movement_is_the_delta()
    {
        // 10% complete → earned 100,000; certified 150,000 → liability −50,000; prior +20,000 → movement −70,000.
        PocCalculation poc = PercentageOfCompletion.Compute(1_000_000m, 800_000m, 80_000m, 150_000m, 20_000m);

        poc.CumulativeAdjustment.Should().Be(-50_000m);
        poc.Movement.Should().Be(-70_000m);
        poc.IsContractLiability.Should().BeTrue();
    }

    [Fact]
    public void Percentage_is_capped_at_100_and_zero_without_a_budget_or_costs()
    {
        PercentageOfCompletion.Compute(500_000m, 400_000m, 450_000m, 0m, 0m).PercentComplete.Should().Be(100m);
        PercentageOfCompletion.Compute(500_000m, 400_000m, 450_000m, 0m, 0m).EarnedRevenue.Should().Be(500_000m);
        PercentageOfCompletion.Compute(500_000m, 0m, 10_000m, 0m, 0m).PercentComplete.Should().Be(0m);
        PercentageOfCompletion.Compute(500_000m, 400_000m, 0m, 0m, 0m).EarnedRevenue.Should().Be(0m);
    }

    [Fact]
    public void No_movement_when_the_position_is_unchanged()
    {
        PocCalculation poc = PercentageOfCompletion.Compute(1_000_000m, 800_000m, 200_000m, 200_000m, 50_000m);
        poc.Movement.Should().Be(0m);
    }
}
