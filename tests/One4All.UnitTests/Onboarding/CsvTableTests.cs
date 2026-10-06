using FluentAssertions;
using One4All.Application.Onboarding;
using Xunit;

namespace One4All.UnitTests.Onboarding;

/// <summary>Covers reading import templates.</summary>
public sealed class CsvTableTests
{
    [Fact]
    public void Rows_are_read_by_header_with_quotes_and_blank_lines_skipped()
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read("Code,Name,Amount,Date\r\nA1,\"Smith, J\",12.50,2026-01-31\r\n\r\nA2,Jones,,\r\n", "code", "name");
        missing.Should().BeEmpty();
        rows.Should().HaveCount(2);
        rows[0].Text("name").Should().Be("Smith, J");
        rows[0].Number("amount").Should().Be(12.5m);
        rows[0].Date("date").Should().Be(new DateOnly(2026, 1, 31));
        (rows[1].LineNumber, rows[1].Number("amount"), rows[1].Text("date")).Should().Be((4, null, null));
    }

    [Fact]
    public void Missing_columns_and_bad_values_are_reported()
    {
        CsvTable.Read("code\nX\n", "code", "name").MissingColumns.Should().Equal("name");
        CsvRow row = CsvTable.Read("amount,date\nabc,31/01/2026\n").Rows.Single();
        FluentActions.Invoking(() => row.Number("amount")).Should().Throw<FormatException>();
        FluentActions.Invoking(() => row.Date("date")).Should().Throw<FormatException>();
    }
}
