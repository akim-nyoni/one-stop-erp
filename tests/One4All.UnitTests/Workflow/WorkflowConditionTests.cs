using FluentAssertions;
using One4All.Application.Workflow;
using Xunit;

namespace One4All.UnitTests.Workflow;

/// <summary>Covers stage condition expressions.</summary>
public sealed class WorkflowConditionTests
{
    private static readonly Dictionary<string, string> It = new() { ["Department"] = "IT", ["Category"] = "Hardware" };

    [Theory]
    [InlineData(null, true)]
    [InlineData("Department = it", true)]
    [InlineData("Department != IT", false)]
    [InlineData("Value > 5000", true)]
    [InlineData("Value >= 6000 and Department = IT", true)]
    [InlineData("Value < 1000 or Category in Software, Hardware", true)]
    [InlineData("Value < 1000 or Category in (Software, Services)", false)]
    [InlineData("Missing = x", false)]
    public void Conditions_read_value_and_attributes(string? condition, bool expected) => WorkflowCondition.Matches(condition, 6_000m, It).Should().Be(expected);

    [Fact]
    public void Unreadable_conditions_are_reported()
    {
        WorkflowCondition.Problem("Department = IT and Value > 10").Should().BeNull();
        WorkflowCondition.Problem("Department IT").Should().Contain("Department IT");
        WorkflowCondition.Problem("Value >").Should().NotBeNull();
    }
}
