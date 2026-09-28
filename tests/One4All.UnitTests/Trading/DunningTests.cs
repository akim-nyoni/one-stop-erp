using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the dunning-level rule.</summary>
public sealed class DunningTests
{
    private static readonly DunningStages Stages = DunningStages.Default; // 7 / 30 / 60

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(29, 1)]
    [InlineData(30, 2)]
    [InlineData(59, 2)]
    [InlineData(60, 3)]
    [InlineData(120, 3)]
    public void Level_escalates_with_days_overdue(int days, int expectedLevel)
    {
        Dunning.LevelFor(days, Stages).Should().Be(expectedLevel);
    }

    [Fact]
    public void Labels_describe_the_levels()
    {
        Dunning.LabelFor(0).Should().Be("Up to date");
        Dunning.LabelFor(1).Should().Be("First reminder");
        Dunning.LabelFor(2).Should().Be("Second reminder");
        Dunning.LabelFor(3).Should().Be("Final notice");
    }
}
