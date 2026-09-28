using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the plain-text receipt layout: content, totals, tenders/change, and fixed width.</summary>
public sealed class PosReceiptFormatterTests
{
    private static PosReceiptModel Sample() => new()
    {
        CompanyName = "Test Trading Co",
        ReceiptNumber = "POS1-001",
        SoldOn = new DateTime(2026, 9, 28, 14, 3, 0),
        CashierName = "Jo Bloggs",
        TerminalName = "Front Till",
        Lines =
        [
            new PosReceiptLine("Coffee", 2m, 6.00m),
            new PosReceiptLine("Muffin", 1m, 3.50m),
        ],
        TotalNet = 9.50m,
        TotalTax = 0m,
        TotalGross = 9.50m,
        Tenders = [new PosReceiptTender("Cash", 10.00m)],
        Change = 0.50m,
    };

    [Fact]
    public void Receipt_contains_heading_lines_totals_and_tender()
    {
        string text = PosReceiptFormatter.ToPlainText(Sample());

        text.Should().Contain("Test Trading Co");
        text.Should().Contain("SALES RECEIPT");
        text.Should().Contain("Receipt: POS1-001");
        text.Should().Contain("2026-09-28 14:03");
        text.Should().Contain("Coffee");
        text.Should().Contain("Muffin");
        text.Should().Contain("TOTAL");
        text.Should().Contain("9.50");
        text.Should().Contain("Cash");
        text.Should().Contain("Change");
        text.Should().Contain("0.50");
        text.Should().Contain("Thank you!");
    }

    [Fact]
    public void Every_line_fits_the_requested_width()
    {
        const int width = 40;
        string text = PosReceiptFormatter.ToPlainText(Sample(), width);

        foreach (string line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            line.Length.Should().BeLessThanOrEqualTo(width);
        }
    }

    [Fact]
    public void An_amount_is_right_aligned_against_its_label()
    {
        string text = PosReceiptFormatter.ToPlainText(Sample(), 40);
        string totalLine = text.Split('\n').Select(l => l.TrimEnd('\r')).First(l => l.StartsWith("TOTAL", StringComparison.Ordinal));

        totalLine.Should().StartWith("TOTAL");
        totalLine.Should().EndWith("9.50");
        totalLine.Length.Should().Be(40);
    }

    [Fact]
    public void Change_is_omitted_when_zero()
    {
        PosReceiptModel noChange = new()
        {
            CompanyName = "Test Trading Co",
            ReceiptNumber = "POS1-002",
            SoldOn = new DateTime(2026, 9, 28, 14, 5, 0),
            Lines = [new PosReceiptLine("Coffee", 1m, 9.50m)],
            TotalNet = 9.50m,
            TotalTax = 0m,
            TotalGross = 9.50m,
            Tenders = [new PosReceiptTender("Card", 9.50m)],
            Change = 0m,
        };

        PosReceiptFormatter.ToPlainText(noChange).Should().NotContain("Change");
    }
}
