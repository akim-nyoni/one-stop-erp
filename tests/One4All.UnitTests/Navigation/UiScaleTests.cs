using FluentAssertions;
using One4All.Application.Localization;
using Xunit;

namespace One4All.UnitTests.Navigation;

/// <summary>Covers the desktop text size steps.</summary>
public sealed class UiScaleTests
{
    [Fact]
    public void Text_size_steps_up_and_down_within_bounds()
    {
        UiScale.Larger(1.0).Should().Be(1.1);
        UiScale.Larger(2.0).Should().Be(2.0);
        UiScale.Smaller(1.25).Should().Be(1.1);
        UiScale.Smaller(0.9).Should().Be(0.9);
        UiScale.Larger(1.2).Should().Be(1.25);
        UiScale.Label(1.25).Should().Be("125%");
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("7", 1.0)]
    [InlineData("abc", 1.0)]
    [InlineData(null, 1.0)]
    public void Stored_sizes_are_read_safely(string? stored, double expected) => UiScale.Parse(stored).Should().Be(expected);
}
