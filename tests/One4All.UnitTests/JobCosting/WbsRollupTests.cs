using FluentAssertions;
using One4All.Application.JobCosting;
using Xunit;

namespace One4All.UnitTests.JobCosting;

/// <summary>Covers the WBS rollup: activity forecasts, phase sums, code order and unassigned costs.</summary>
public sealed class WbsRollupTests
{
    [Fact]
    public void Activities_forecast_and_phases_sum_their_children()
    {
        WbsElementInput[] elements =
        [
            new("2", "Superstructure", 0m, null, null),
            new("1", "Groundworks", 0m, null, null),
            new("1.10", "Drainage", 2_000m, "Drains", null),
            new("1.9", "Excavation", 5_000m, "Dig", 500m),
            new("2.1", "Frame", 10_000m, "Steel", null),
        ];
        Dictionary<string, decimal> actual = new() { ["Dig"] = 6_000m, ["Drains"] = 500m, ["Steel"] = 1_000m, ["Sundries"] = 50m };
        Dictionary<string, decimal> committed = new() { ["Steel"] = 7_000m };

        (IReadOnlyList<WbsLine> lines, WbsLine total) = WbsRollup.Build(elements, actual, committed);

        lines.Select(l => l.Code).Should().Equal("1", "1.9", "1.10", "2", "2.1", WbsRollup.Unassigned);

        // Excavation overran its budget and the site expects 500 more: forecast 6,500 against 5,000.
        WbsLine dig = lines.Single(l => l.Code == "1.9");
        dig.ForecastFinalCost.Should().Be(6_500m);
        dig.Variance.Should().Be(-1_500m);
        dig.Level.Should().Be(2);

        // With no estimate the budget left is still to spend; never negative.
        lines.Single(l => l.Code == "1.10").EstimateToComplete.Should().Be(1_500m);
        lines.Single(l => l.Code == "2.1").Should().Be(new WbsLine("2.1", "Frame", 2, 10_000m, 1_000m, 7_000m, 2_000m, 10_000m, 0m, 10m));

        WbsLine groundworks = lines.Single(l => l.Code == "1");
        (groundworks.Budget, groundworks.Actual, groundworks.ForecastFinalCost).Should().Be((7_000m, 6_500m, 8_500m));
        lines.Last().Actual.Should().Be(50m);
        (total.Budget, total.Actual, total.Committed, total.ForecastFinalCost).Should().Be((17_000m, 7_550m, 7_000m, 18_550m));
    }
}
