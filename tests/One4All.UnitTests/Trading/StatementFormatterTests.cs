using FluentAssertions;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the plain-text statement layout and the ageing buckets.</summary>
public sealed class StatementFormatterTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 30);

    private static TraderStatement Sample() => new("CUST1", "Acme Retail Ltd", "USD",
        [
            new StatementLineRow(new DateOnly(2026, 1, 10), OpenItemType.Invoice, "INV-1", new DateOnly(2026, 2, 9), 500m, 500m), // 141 days overdue
            new StatementLineRow(new DateOnly(2026, 3, 20), OpenItemType.Invoice, "INV-2", new DateOnly(2026, 4, 19), 300m, 300m), // 72 days
            new StatementLineRow(new DateOnly(2026, 4, 25), OpenItemType.CreditNote, "CRN-1", new DateOnly(2026, 5, 25), -50m, -50m), // 36 days, nets in 31-60
            new StatementLineRow(new DateOnly(2026, 5, 15), OpenItemType.Invoice, "INV-3", new DateOnly(2026, 6, 14), 200m, 200m), // 16 days
            new StatementLineRow(new DateOnly(2026, 6, 20), OpenItemType.Invoice, "INV-4", new DateOnly(2026, 7, 20), 120m, 120m), // not due
            new StatementLineRow(new DateOnly(2026, 6, 1), OpenItemType.Receipt, "RCT-1", new DateOnly(2026, 6, 1), -100m, 0m), // fully allocated
        ],
        1070m);

    [Fact]
    public void Ageing_buckets_by_days_past_due_and_nets_credits()
    {
        StatementAgeing ageing = StatementAgeing.Of(Sample(), AsAt);

        ageing.Current.Should().Be(120m);
        ageing.Days1To30.Should().Be(200m);
        ageing.Days31To60.Should().Be(-50m);
        ageing.Days61To90.Should().Be(300m);
        ageing.Over90.Should().Be(500m);
        ageing.Total.Should().Be(1070m);
    }

    [Fact]
    public void Statement_contains_heading_lines_balance_and_ageing()
    {
        string text = StatementFormatter.ToPlainText(Sample(), "Test Trading Co", AsAt);

        text.Should().Contain("Test Trading Co");
        text.Should().Contain("STATEMENT OF ACCOUNT");
        text.Should().Contain("To: Acme Retail Ltd (CUST1)");
        text.Should().Contain("Statement date: 2026-06-30").And.Contain("Currency: USD");
        text.Should().Contain("2026-01-10 INV INV-1").And.Contain("CRN CRN-1").And.Contain("RCT RCT-1");
        text.Should().Contain("BALANCE DUE (USD)").And.Contain("1,070.00");
        text.Should().Contain("Over 90 days").And.Contain("500.00");
    }

    [Fact]
    public void Every_line_fits_the_requested_width_and_narrow_widths_are_clamped()
    {
        const int width = 48;
        string[] lines = StatementFormatter.ToPlainText(Sample(), "Test Trading Co", AsAt, width).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().OnlyContain(l => l.TrimEnd('\r').Length <= width);

        string[] clamped = StatementFormatter.ToPlainText(Sample(), "Test Trading Co", AsAt, 10).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        clamped.Should().OnlyContain(l => l.TrimEnd('\r').Length <= 40);
    }
}
