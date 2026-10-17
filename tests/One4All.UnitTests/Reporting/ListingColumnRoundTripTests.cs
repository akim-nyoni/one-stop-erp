using FluentAssertions;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the report designer's core transformation: a listing definition can be decomposed into columns
/// and rebuilt through <see cref="SystemReports.Listing"/> without loss - extract → edit → rebuild.
/// </summary>
public class ListingColumnRoundTripTests
{
    [Fact]
    public void Columns_extracted_from_a_system_listing_round_trip_back_to_the_same_layout()
    {
        // A representative money-bearing listing with parameters (the Journal Daybook).
        ReportDefinition original = SystemReports.JournalDaybook();

        SystemReports.Column[] columns = SystemReports.ExtractListingColumns(original);

        columns.Should().NotBeEmpty();
        // The money column is recognised as such (right-aligned, N2); text columns are not.
        columns.Should().Contain(c => c.Field == "Amount" && c.Money);
        columns.Should().Contain(c => c.Field == "JournalNumber" && !c.Money);

        // Rebuild through the same public builder the designer uses.
        ReportDefinition rebuilt = SystemReports.Listing(
            original.Code, original.Name, original.Module ?? string.Empty, original.Description ?? string.Empty,
            SystemReports.CommandOf(original), columns,
            original.Parameters.Select(p => (p.Name, p.Type)).ToArray());

        SystemReports.Column[] rebuiltColumns = SystemReports.ExtractListingColumns(rebuilt);
        rebuiltColumns.Should().BeEquivalentTo(columns, o => o.WithStrictOrdering());
        SystemReports.CommandOf(rebuilt).Should().Be(SystemReports.CommandOf(original));
        rebuilt.Parameters.Select(p => p.Name).Should().BeEquivalentTo(original.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Reordering_and_editing_columns_is_preserved_through_a_rebuild()
    {
        ReportDefinition original = SystemReports.ChartOfAccounts();
        List<SystemReports.Column> columns = [.. SystemReports.ExtractListingColumns(original)];

        // Edit a header and move the first column to the end (what the designer's drag-reorder does).
        columns[0] = columns[0] with { Header = "Ledger Account" };
        SystemReports.Column moved = columns[0];
        columns.RemoveAt(0);
        columns.Add(moved);

        ReportDefinition rebuilt = SystemReports.Listing(
            original.Code, original.Name, original.Module ?? string.Empty, original.Description ?? string.Empty,
            SystemReports.CommandOf(original), [.. columns],
            original.Parameters.Select(p => (p.Name, p.Type)).ToArray());

        SystemReports.Column[] result = SystemReports.ExtractListingColumns(rebuilt);
        result.Should().BeEquivalentTo(columns, o => o.WithStrictOrdering());
        result[^1].Header.Should().Be("Ledger Account"); // moved to the end, edited header preserved
    }
}
