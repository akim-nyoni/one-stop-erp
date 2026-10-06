using FluentAssertions;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers choosing the enforcement rule and a document's overall outcome.</summary>
public sealed class BudgetGateTests
{
    private static readonly BudgetEnforcementRule[] Rules =
    [
        new(1, null, null, 0m, BudgetAction.Warn),
        new(1, null, "6", 1_000m, BudgetAction.RequireApproval),
        new(1, null, "68", 0m, BudgetAction.Block),
        new(1, "ADM", null, 0m, BudgetAction.Allow),
    ];

    [Fact]
    public void The_most_specific_rule_wins()
    {
        BudgetGate.ActionFor(Rules, null, "4000", 10m).Should().Be(BudgetAction.Warn);
        BudgetGate.ActionFor(Rules, null, "6000", 999m).Should().Be(BudgetAction.Warn);
        BudgetGate.ActionFor(Rules, null, "6000", 1_000m).Should().Be(BudgetAction.RequireApproval);
        BudgetGate.ActionFor(Rules, null, "6800", 5m).Should().Be(BudgetAction.Block);
        BudgetGate.ActionFor(Rules, "adm", "6800", 5_000m).Should().Be(BudgetAction.Allow);
        BudgetGate.ActionFor([], null, "6000", 1m).Should().Be(BudgetAction.Allow);
    }

    [Fact]
    public void Only_overrunning_lines_count_and_the_strictest_wins()
    {
        BudgetGate.Overall([(0m, BudgetAction.Block), (10m, BudgetAction.Warn)]).Should().Be(BudgetAction.Warn);
        BudgetGate.Overall([(5m, BudgetAction.RequireApproval), (10m, BudgetAction.Warn)]).Should().Be(BudgetAction.RequireApproval);
        BudgetGate.Overall([(0m, BudgetAction.Block)]).Should().Be(BudgetAction.Allow);
    }
}
