using FluentAssertions;
using One4All.Application.Reporting;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers filtering any report's rows by its columns: branch, currency and so on, ignoring filters on columns it lacks.</summary>
public sealed class ReportRowFiltersTests
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows =
    [
        new Dictionary<string, object?> { ["Branch"] = "HQ", ["Currency"] = "USD", ["Amount"] = 10m },
        new Dictionary<string, object?> { ["Branch"] = "HQ", ["Currency"] = "ZAR", ["Amount"] = 20m },
        new Dictionary<string, object?> { ["Branch"] = "North", ["Currency"] = "USD", ["Amount"] = 30m },
    ];

    [Fact]
    public void Rows_are_kept_by_every_filter_on_a_column_they_have()
    {
        IReadOnlyDictionary<string, object?> filters = ReportRowFilters.Parse("branch = hq; Currency=USD; Fund=GEN; Broken; Empty=");
        filters.Keys.Should().BeEquivalentTo(["Filter.branch", "Filter.Currency", "Filter.Fund"]);
        ReportRowFilters.Apply(Rows, filters).Select(r => r["Amount"]).Should().Equal(10m);
        ReportRowFilters.Apply(Rows, ReportRowFilters.Parse("Fund=GEN")).Should().HaveCount(3, "the report has no fund column");
        ReportRowFilters.Apply(Rows, new Dictionary<string, object?> { ["Branch"] = "North" }).Should().HaveCount(3, "only Filter.-named parameters filter");
    }
}
