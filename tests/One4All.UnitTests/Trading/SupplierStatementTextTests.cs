using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers reading pasted supplier-statement text.</summary>
public sealed class SupplierStatementTextTests
{
    [Fact]
    public void Comma_text_with_quotes_and_bracketed_credits_parses()
    {
        const string text = "S-101,2026-05-02,600.00\n\"S 102\",2026-05-09,\"1,275.00\"\nPayment - thank you,2026-05-22,(300.00)\n";

        SupplierStatementParseResult result = SupplierStatementText.Parse(text);

        result.IsClean.Should().BeTrue();
        result.Lines.Should().Equal(
            new SupplierStatementLine("S-101", new DateOnly(2026, 5, 2), 600.00m),
            new SupplierStatementLine("S 102", new DateOnly(2026, 5, 9), 1275.00m),
            new SupplierStatementLine("Payment - thank you", new DateOnly(2026, 5, 22), -300.00m));
    }

    [Fact]
    public void Tab_separated_spreadsheet_paste_with_header_and_uk_dates()
    {
        const string text = "Doc\tDate\tAmount\nINV-9\t02/05/2026\t45.50\n";

        SupplierStatementParseResult result = SupplierStatementText.Parse(text, "dd/MM/yyyy", hasHeader: true, delimiter: '\t');

        result.IsClean.Should().BeTrue();
        result.Lines.Should().ContainSingle().Which.Should().Be(new SupplierStatementLine("INV-9", new DateOnly(2026, 5, 2), 45.50m));
    }

    [Fact]
    public void Bad_lines_are_reported_not_thrown()
    {
        const string text = "S-1,2026-05-02,10\nS-2,not a date,10\n,2026-05-03,10\n";

        SupplierStatementParseResult result = SupplierStatementText.Parse(text);

        result.IsClean.Should().BeFalse();
        result.Lines.Should().ContainSingle(l => l.DocumentNumber == "S-1");
        result.Errors.Should().HaveCount(2);
    }
}
