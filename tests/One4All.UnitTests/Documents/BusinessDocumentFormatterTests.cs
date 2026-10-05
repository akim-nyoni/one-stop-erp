using FluentAssertions;
using One4All.Application.Documents;
using Xunit;

namespace One4All.UnitTests.Documents;

public sealed class BusinessDocumentFormatterTests
{
    private static BusinessDocument Sample(bool prices) => new()
    {
        Title = "Delivery note", CompanyName = "Acme Ltd", CompanyTaxNumber = "T-1", PartyLabel = "Deliver to", PartyName = "Builder",
        Fields = [("Delivery", "D1"), ("Date", "2026-03-08"), ("Vehicle", "TRK1")], ShowPrices = prices,
        Lines = [new BusinessDocumentLine("BRK", "Face brick", 400m, 0.9m, 360m)], Totals = prices ? [("TOTAL", 360m)] : [], Notes = ["Check the goods."], Signatures = ["Received by"],
    };

    [Fact]
    public void Renders_header_parties_fields_lines_totals_and_signatures()
    {
        string text = BusinessDocumentFormatter.ToPlainText(Sample(prices: true));
        string[] lines = text.Split(Environment.NewLine);
        lines[0].Trim().Should().Be("Acme Ltd");
        lines[1].Trim().Should().Be("Tax No. T-1");
        lines[2].Trim().Should().Be("DELIVERY NOTE");
        text.Should().Contain("Deliver to: Builder").And.Contain("Delivery: D1").And.Contain("Date: 2026-03-08").And.Contain(" 1. BRK Face brick").And.Contain("360.00")
            .And.Contain("Check the goods.").And.Contain("Received by: ______________________");
        lines.Where(l => l.Length > 0).Should().OnlyContain(l => l.Length <= BusinessDocumentFormatter.DefaultWidth);
    }

    [Fact]
    public void Hides_prices_when_the_document_does_not_show_them()
        => BusinessDocumentFormatter.ToPlainText(Sample(prices: false)).Should().NotContain("0.90").And.NotContain("360.00").And.NotContain("Price");

    [Fact]
    public void Shelf_labels_lay_out_in_rows_with_a_barcode_line()
    {
        string sheet = ShelfLabelFormatter.ToPlainText([new ShelfLabel("A1", "Apples", 1.5m, "USD", "600123"), new ShelfLabel("B2", "Bananas", 2m, "USD", "b2"), new ShelfLabel("C3", "Cherries", 3m, "USD", "C3")], perRow: 2, labelWidth: 24);
        sheet.Should().Contain("| Apples").And.Contain("| USD 1.50").And.Contain("*600123*").And.Contain("*B2*");
        sheet.Split(Environment.NewLine).Count(l => l.StartsWith('+')).Should().Be(4); // two rows, top and bottom borders
    }
}
