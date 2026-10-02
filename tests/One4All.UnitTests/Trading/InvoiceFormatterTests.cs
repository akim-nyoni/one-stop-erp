using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the plain-text invoice / credit note layout and totals.</summary>
public sealed class InvoiceFormatterTests
{
    private static InvoiceDocument Invoice() => new(false, "Test Trading Co", "VAT-123", "CUST1", "Acme Retail Ltd", "VAT-999",
        "INV-00042", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "USD",
        [new InvoiceDocumentLine("Sales", 1_000.00m), new InvoiceDocumentLine("Delivery", 50.00m)],
        Tax: 157.50m, Outstanding: 1_207.50m, Narrative: "July order");

    [Fact]
    public void Totals_derive_from_lines_and_tax()
    {
        InvoiceDocument doc = Invoice();
        doc.Net.Should().Be(1_050.00m);
        doc.Gross.Should().Be(1_207.50m);
    }

    [Fact]
    public void Tax_invoice_shows_parties_lines_totals_and_terms()
    {
        string text = InvoiceFormatter.ToPlainText(Invoice());

        text.Should().Contain("Test Trading Co").And.Contain("VAT No. VAT-123").And.Contain("TAX INVOICE");
        text.Should().Contain("To: Acme Retail Ltd (CUST1)").And.Contain("Customer VAT No. VAT-999");
        text.Should().Contain("Number: INV-00042").And.Contain("Date: 2026-07-01").And.Contain("Due: 2026-07-31");
        text.Should().Contain("July order");
        text.Should().Contain(" 1. Sales").And.Contain(" 2. Delivery");
        text.Should().Contain("1,050.00").And.Contain("157.50").And.Contain("1,207.50");
        text.Should().Contain("Balance due").And.Contain("Please pay by 2026-07-31 quoting INV-00042.");
    }

    [Fact]
    public void Credit_note_has_no_due_date_or_terms_and_plain_invoice_without_vat_number()
    {
        InvoiceDocument credit = Invoice() with { IsCreditNote = true, DocumentNumber = "CRN-7", Outstanding = 300m };
        string text = InvoiceFormatter.ToPlainText(credit);
        text.Should().Contain("CREDIT NOTE").And.Contain("Unapplied credit");
        text.Should().NotContain("Due:").And.NotContain("Please pay");

        string plain = InvoiceFormatter.ToPlainText(Invoice() with { CompanyTaxNumber = null, CustomerTaxNumber = null });
        plain.Should().Contain("INVOICE").And.NotContain("TAX INVOICE").And.NotContain("VAT No.");
    }

    [Fact]
    public void Every_line_fits_the_requested_width()
    {
        const int width = 48;
        string[] lines = InvoiceFormatter.ToPlainText(Invoice(), width).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().OnlyContain(l => l.TrimEnd('\r').Length <= width);
    }
}
