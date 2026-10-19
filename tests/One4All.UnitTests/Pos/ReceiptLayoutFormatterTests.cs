using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Receipt layouts: 80mm and 58mm widths, header and footer lines, what is shown, and the framed preview.</summary>
public sealed class ReceiptLayoutFormatterTests
{
    private static readonly PosReceiptModel Sample = new()
    {
        CompanyName = "Corner Shop", CompanyTaxNumber = "TX-123", ReceiptNumber = "R1", SoldOn = new DateTime(2026, 3, 2, 10, 30, 0), CashierName = "Ann", TerminalName = "Till 1",
        Lines = [new PosReceiptLine("Bread", 2m, 3m, 1.5m)], TotalNet = 2.6m, TotalTax = 0.4m, TotalGross = 3m, Tenders = [new PosReceiptTender("Cash", 5m)], Change = 2m,
    };

    [Fact]
    public void An_80mm_layout_is_48_characters_with_its_own_lines()
    {
        string text = PosReceiptFormatter.ToPlainText(Sample, new ReceiptOptions(ReceiptOptions.WidthFor(80), "TAX INVOICE", ["12 Main St", "Tel 555"], ["Come again"], ShowUnitPrice: true,
            ShowTaxNumber: true, ShowTaxSummary: false));
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        lines.Should().OnlyContain(l => l.Length <= 48).And.Contain(l => l.Length == 48);
        text.Should().Contain("Tax no: TX-123").And.Contain("12 Main St").And.Contain("TAX INVOICE").And.Contain("  2 x 1.50").And.Contain("Come again")
            .And.NotContain("Subtotal").And.NotContain("Thank you!");
    }

    [Fact]
    public void A_58mm_layout_is_32_characters_and_the_preview_draws_the_paper()
    {
        string text = PosReceiptFormatter.ToPlainText(Sample, new ReceiptOptions(ReceiptOptions.WidthFor(58), ShowCashier: false));
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Should().OnlyContain(l => l.Length <= 32);
        text.Should().NotContain("Cashier").And.Contain("Till 1").And.Contain("Thank you!");
        string framed = PosReceiptFormatter.Framed(text, 32);
        framed.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Should().OnlyContain(l => l.Length == 36);
    }

    [Fact]
    public void The_default_layout_keeps_the_standard_receipt()
        => PosReceiptFormatter.ToPlainText(Sample, 40).Should().Be(PosReceiptFormatter.ToPlainText(Sample, new ReceiptOptions(40)));
}
