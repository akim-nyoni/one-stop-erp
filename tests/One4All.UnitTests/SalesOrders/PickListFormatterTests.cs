using FluentAssertions;
using One4All.Application.SalesOrders;
using Xunit;

namespace One4All.UnitTests.SalesOrders;

/// <summary>Covers the plain-text pick list layout, shortage marking and fixed width.</summary>
public sealed class PickListFormatterTests
{
    private static PickListDocument Sample() => new("Test Trading Co", "SO-00042", new DateOnly(2026, 7, 3), "CUST1", "Acme Retail Ltd", "WH1",
        [
            new PickListLine("WIDGET", "Blue widget", 10m, 25m),
            new PickListLine("GADGET", "Gadget, large", 6m, 4m),
        ]);

    [Fact]
    public void Short_is_the_unsupplied_quantity_and_completeness_follows_it()
    {
        PickListDocument doc = Sample();
        doc.Lines[0].Short.Should().Be(0m);
        doc.Lines[1].Short.Should().Be(2m);
        doc.IsComplete.Should().BeFalse();

        PickListDocument full = doc with { Lines = [doc.Lines[0]] };
        full.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Pick_list_contains_heading_lines_and_shortage_markers()
    {
        string text = PickListFormatter.ToPlainText(Sample());

        text.Should().Contain("Test Trading Co").And.Contain("PICK LIST");
        text.Should().Contain("Order: SO-00042").And.Contain("2026-07-03");
        text.Should().Contain("Customer: Acme Retail Ltd (CUST1)").And.Contain("Warehouse: WH1");
        text.Should().Contain("WIDGET Blue widget").And.Contain("[    ]");
        text.Should().Contain("GADGET Gadget, large").And.Contain("SHORT 2.00");
        text.Should().Contain("2 line(s)").And.Contain("PART PICK - SHORTAGES");
        text.Should().Contain("Picked by:");
    }

    [Fact]
    public void Every_line_fits_the_requested_width()
    {
        const int width = 50;
        string[] lines = PickListFormatter.ToPlainText(Sample(), width).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().OnlyContain(l => l.TrimEnd('\r').Length <= width);

        string[] clamped = PickListFormatter.ToPlainText(Sample(), 5).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        clamped.Should().OnlyContain(l => l.TrimEnd('\r').Length <= 40);
    }
}
