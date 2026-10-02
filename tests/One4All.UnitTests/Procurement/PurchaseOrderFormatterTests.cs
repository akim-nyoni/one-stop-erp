using FluentAssertions;
using One4All.Application.Procurement;
using Xunit;

namespace One4All.UnitTests.Procurement;

/// <summary>Covers the plain-text purchase order layout and totals.</summary>
public sealed class PurchaseOrderFormatterTests
{
    private static PurchaseOrderDocument Sample() => new("Test Trading Co", "VAT-123", "SUP1", "Parts Supplier Ltd", "PO-00007", new DateOnly(2026, 7, 2), "USD", 45, "Open",
        [new PurchaseOrderDocumentLine("BOLT", "M8 bolt", 100m, 1.25m), new PurchaseOrderDocumentLine("NUT", "M8 nut, zinc", 100m, 0.40m)]);

    [Fact]
    public void Line_totals_and_order_total_derive_from_quantity_and_price()
    {
        PurchaseOrderDocument doc = Sample();
        doc.Lines[0].LineTotal.Should().Be(125.00m);
        doc.Lines[1].LineTotal.Should().Be(40.00m);
        doc.Total.Should().Be(165.00m);
    }

    [Fact]
    public void Purchase_order_shows_parties_lines_total_and_terms()
    {
        string text = PurchaseOrderFormatter.ToPlainText(Sample());

        text.Should().Contain("Test Trading Co").And.Contain("VAT No. VAT-123").And.Contain("PURCHASE ORDER");
        text.Should().Contain("To: Parts Supplier Ltd (SUP1)");
        text.Should().Contain("Order: PO-00007").And.Contain("Date: 2026-07-02").And.Contain("Status: Open");
        text.Should().Contain(" 1. BOLT M8 bolt").And.Contain(" 2. NUT M8 nut, zinc");
        text.Should().Contain("125.00").And.Contain("40.00").And.Contain("165.00");
        text.Should().Contain("Payment terms: 45 days").And.Contain("quote PO-00007");
    }

    [Fact]
    public void Every_line_fits_the_requested_width()
    {
        const int width = 56;
        string[] lines = PurchaseOrderFormatter.ToPlainText(Sample(), width).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().OnlyContain(l => l.TrimEnd('\r').Length <= width);
    }
}
