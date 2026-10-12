using FluentAssertions;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers judging slump and strength results.</summary>
public sealed class ConcreteTestRulesTests
{
    [Theory]
    [InlineData(ConcreteTestKind.Slump, 95, 75, 25, true)]
    [InlineData(ConcreteTestKind.Slump, 110, 75, 25, false)]
    [InlineData(ConcreteTestKind.Cube7Day, 17.5, 17.5, 0, true)]
    [InlineData(ConcreteTestKind.Cube28Day, 23.9, 25, 0, false)]
    public void Results_are_judged_against_the_mix(ConcreteTestKind kind, double result, double specified, double tolerance, bool passes)
        => ConcreteTest.Passes(kind, (decimal)result, (decimal)specified, (decimal)tolerance).Should().Be(passes);
}
