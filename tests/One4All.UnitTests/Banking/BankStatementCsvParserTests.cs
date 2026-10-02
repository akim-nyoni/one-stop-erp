using FluentAssertions;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers CSV statement parsing: layouts, quoting, number formats and error reporting.</summary>
public sealed class BankStatementCsvParserTests
{
    [Fact]
    public void Simple_layout_parses_signed_amounts_quotes_and_references()
    {
        const string csv = "Date,Description,Amount,Reference\r\n" +
                           "2026-03-01,\"ACME, LTD payment\",1500.00,INV-77\r\n" +
                           "2026-03-02,Bank charges,-12.50,\r\n" +
                           "\r\n" +
                           "2026-03-03,\"Said \"\"thanks\"\"\",\"(2,000.00)\",REF9\r\n";

        BankCsvParseResult result = BankStatementCsvParser.Parse(csv, BankCsvLayout.Simple);

        result.IsClean.Should().BeTrue();
        result.Lines.Should().HaveCount(3);
        result.Lines[0].Should().Be(new StatementLineImport(new DateOnly(2026, 3, 1), "ACME, LTD payment", 1500.00m, "INV-77"));
        result.Lines[1].Should().Be(new StatementLineImport(new DateOnly(2026, 3, 2), "Bank charges", -12.50m, null));
        result.Lines[2].Should().Be(new StatementLineImport(new DateOnly(2026, 3, 3), "Said \"thanks\"", -2000.00m, "REF9"));
    }

    [Fact]
    public void Debit_credit_layout_with_uk_dates_and_semicolons()
    {
        BankCsvLayout layout = new() { Delimiter = ';', DateColumn = 0, DateFormat = "dd/MM/yyyy", DescriptionColumn = 2, DebitColumn = 3, CreditColumn = 4, ReferenceColumn = 1, HasHeader = false };
        const string csv = "05/03/2026;CHQ 1001;Rent;3 000,00;\n" +
                           "06/03/2026;;Customer receipt;;850,00\n";

        BankCsvParseResult result = BankStatementCsvParser.Parse(csv.Replace(",00", ".00", StringComparison.Ordinal), layout);

        result.IsClean.Should().BeTrue();
        result.Lines.Should().HaveCount(2);
        result.Lines[0].Amount.Should().Be(-3000.00m);
        result.Lines[0].Reference.Should().Be("CHQ 1001");
        result.Lines[1].Amount.Should().Be(850.00m);
        result.Lines[1].Reference.Should().BeNull();
    }

    [Fact]
    public void Bad_lines_are_reported_with_their_line_numbers_and_good_ones_still_parse()
    {
        const string csv = "Date,Description,Amount\n" +
                           "2026-03-01,Good,10.00\n" +
                           "03/01/2026,Bad date,10.00\n" +
                           "2026-03-03,Bad amount,ten\n" +
                           "2026-03-04,Zero,0.00\n" +
                           "2026-03-05,Short\n";

        BankCsvParseResult result = BankStatementCsvParser.Parse(csv, new BankCsvLayout { DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2 });

        result.IsClean.Should().BeFalse();
        result.Lines.Should().ContainSingle(l => l.Description == "Good");
        result.Errors.Select(e => e.LineNumber).Should().Equal(3, 4, 5, 6);
        result.Errors[0].Reason.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public void Layout_without_an_amount_source_is_rejected_up_front()
    {
        BankCsvParseResult result = BankStatementCsvParser.Parse("2026-03-01,x,1\n", new BankCsvLayout { AmountColumn = null, DebitColumn = 2 });

        result.IsClean.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.LineNumber == 0);
        result.Lines.Should().BeEmpty();
    }
}
