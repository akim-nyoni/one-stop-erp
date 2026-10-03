using FluentAssertions;
using One4All.Application.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers reading manual journals from CSV: grouping by reference, optional columns and every refusal.</summary>
public sealed class JournalCsvParserTests
{
    [Fact]
    public void Rows_sharing_a_reference_form_one_balanced_journal()
    {
        const string csv = """
            Date,Reference,Account,Cost Centre,Debit,Credit,Narrative
            2026-03-10,J1,6800,ADM,"1,200.00",,"Stationery, March"
            2026-03-10,J1,2300,,,1200.00,
            10/03/2026,J2,6000,,50,,Depreciation
            10/03/2026,J2,1490,,,50,
            """;

        JournalCsvResult result = JournalCsvParser.Parse(csv);

        result.IsClean.Should().BeTrue();
        result.Journals.Select(j => (j.Reference, j.Date, j.Narrative, j.TotalDebit)).Should().Equal(
            ("J1", new DateOnly(2026, 3, 10), "Stationery, March", 1_200m),
            ("J2", new DateOnly(2026, 3, 10), "Depreciation", 50m));
        result.Journals[0].Lines[0].Should().Be(new JournalCsvLine(2, "6800", "ADM", null, 1_200m, 0m, "Stationery, March"));
    }

    [Fact]
    public void A_header_without_the_required_columns_is_refused()
    {
        JournalCsvParser.Parse("Date,Account,Debit\n2026-03-10,6800,5").Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("REFERENCE").And.Contain("CREDIT");
    }

    [Fact]
    public void Each_bad_line_and_unbalanced_journal_is_reported()
    {
        const string csv = """
            Reference,Date,Account,Debit,Credit
            J1,2026-03-10,6800,100,
            J1,2026-03-10,2300,,90
            J2,2026-13-01,6800,5,
            J3,2026-03-10,6800,5,5
            J4,2026-03-10,6800,5,
            J4,2026-03-11,2300,,5
            ,2026-03-10,6800,5,
            """;

        JournalCsvResult result = JournalCsvParser.Parse(csv);

        result.IsClean.Should().BeFalse();
        result.Errors.Select(e => e.LineNumber).Should().Equal(2, 4, 5, 6, 7, 8); // J4 is both mixed-date and, without line 7, unbalanced
    }

    [Fact]
    public void A_file_with_only_a_header_is_refused()
    {
        JournalCsvParser.Parse("Reference,Date,Account,Debit,Credit\n").IsClean.Should().BeFalse();
    }
}
