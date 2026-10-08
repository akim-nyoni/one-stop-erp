using FluentAssertions;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers typing report parameter entries: single values by type, multi-select lists as arrays, and cascading lookups waiting on their parents.</summary>
public sealed class ReportParameterValuesTests
{
    [Fact]
    public void Single_values_take_their_type_and_blank_is_null()
    {
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Date }, " 2026-10-08 ").Should().Be(new DateOnly(2026, 10, 8));
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Integer }, "42").Should().Be(42L);
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Decimal }, "1,250.50").Should().Be(1250.50m);
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Boolean }, "true").Should().Be(true);
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Integer }, "many").Should().Be("many");
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.String }, "   ").Should().BeNull();
    }

    [Fact]
    public void Multi_select_entries_become_typed_arrays()
    {
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.String, MultiSelect = true }, "HQ, NORTH;SOUTH").Should().BeEquivalentTo(new[] { "HQ", "NORTH", "SOUTH" });
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Integer, MultiSelect = true }, "3,5").Should().BeEquivalentTo(new[] { 3L, 5L });
        ReportParameterValues.Parse(new ReportParameter { Type = ParameterType.Date, MultiSelect = true }, "2026-01-31,2026-02-28").Should()
            .BeEquivalentTo(new[] { new DateTime(2026, 1, 31), new DateTime(2026, 2, 28) });

        ReportDefinition def = new() { Parameters = [new ReportParameter { Name = "Branches", MultiSelect = true }, new ReportParameter { Name = "From", Type = ParameterType.Date }] };
        IReadOnlyDictionary<string, object?> query = ReportParameterValues.ForQuery(def, new Dictionary<string, object?> { ["Branches"] = "A,B", ["From"] = new DateOnly(2026, 1, 1) });
        query["Branches"].Should().BeEquivalentTo(new[] { "A", "B" });
        query["From"].Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void A_cascading_lookup_waits_for_its_parents()
    {
        ReportParameter warehouse = new() { Name = "Warehouse", LookupQuery = "SELECT 1", DependsOn = ["Branch", "Region"] };
        ReportParameterValues.WaitingOn(warehouse, new Dictionary<string, object?> { ["Branch"] = "HQ", ["Region"] = "" }).Should().Equal("Region");
        ReportParameterValues.WaitingOn(warehouse, new Dictionary<string, object?> { ["Branch"] = "HQ", ["Region"] = "N" }).Should().BeEmpty();
    }
}
