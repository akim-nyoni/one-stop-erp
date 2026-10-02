using FluentAssertions;
using One4All.Application.JobCosting;
using Xunit;

namespace One4All.UnitTests.JobCosting;

/// <summary>Covers the onerous-contract (expected loss) maths.</summary>
public sealed class ExpectedLossTests
{
    [Fact]
    public void Profitable_contract_has_no_expected_loss_and_releases_any_prior_provision()
    {
        ExpectedLossCalculation calc = ExpectedLoss.Compute(1_000_000m, 800_000m, 30_000m);

        calc.ExpectedLoss.Should().Be(0m);
        calc.IsOnerous.Should().BeFalse();
        calc.Movement.Should().Be(-30_000m);
    }

    [Fact]
    public void Onerous_contract_provides_the_whole_loss_at_once()
    {
        ExpectedLossCalculation calc = ExpectedLoss.Compute(1_000_000m, 1_120_000m, 0m);

        calc.ExpectedLoss.Should().Be(120_000m);
        calc.IsOnerous.Should().BeTrue();
        calc.Movement.Should().Be(120_000m);
    }

    [Fact]
    public void Movement_is_only_the_change_since_the_prior_provision()
    {
        ExpectedLoss.Compute(1_000_000m, 1_120_000m, 120_000m).Movement.Should().Be(0m);
        ExpectedLoss.Compute(1_000_000m, 1_150_000m, 120_000m).Movement.Should().Be(30_000m);
        ExpectedLoss.Compute(1_000_000m, 1_050_000m, 120_000m).Movement.Should().Be(-70_000m);
    }
}
