using FluentAssertions;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

public sealed class PayslipCurrencySplitTests
{
    [Fact]
    public void Format_writes_the_foreign_portions_in_code_order_without_base_or_nil_entries()
    {
        PayslipCurrencySplit.Format(new Dictionary<string, decimal> { [string.Empty] = 100m, ["ZWG"] = 30m, ["EUR"] = 12.5m, ["ZAR"] = 0m })
            .Should().Be("EUR=12.50;ZWG=30.00");
        PayslipCurrencySplit.Format(new Dictionary<string, decimal> { [string.Empty] = 100m }).Should().BeNull();
    }

    [Fact]
    public void Parse_reads_what_format_wrote_and_skips_what_does_not_read()
    {
        PayslipCurrencySplit.Parse("EUR=12.50;ZWG=30.00").Should().BeEquivalentTo(new Dictionary<string, decimal> { ["EUR"] = 12.5m, ["ZWG"] = 30m });
        PayslipCurrencySplit.Parse("zwg=1.25; junk ;=4;EUR=x").Should().BeEquivalentTo(new Dictionary<string, decimal> { ["ZWG"] = 1.25m });
        PayslipCurrencySplit.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public void Apportion_rounds_the_foreign_portions_and_gives_the_rest_to_base()
    {
        Dictionary<string, decimal> portions = PayslipCurrencySplit.Apportion(100m, new Dictionary<string, decimal> { [string.Empty] = 2m, ["ZWG"] = 1m });
        portions["ZWG"].Should().Be(33.33m);
        portions[string.Empty].Should().Be(66.67m);
        PayslipCurrencySplit.Apportion(10m, new Dictionary<string, decimal> { ["ZWG"] = 0m }).Should().BeEquivalentTo(new Dictionary<string, decimal> { [string.Empty] = 10m });
    }

    [Fact]
    public void Exact_keeps_the_portions_and_puts_the_rounding_difference_in_base()
    {
        Dictionary<string, decimal> portions = PayslipCurrencySplit.Exact(163.33m, new Dictionary<string, decimal> { [string.Empty] = 133.3333m, ["ZWG"] = 30.004m });
        portions["ZWG"].Should().Be(30m);
        portions[string.Empty].Should().Be(133.33m);
    }
}
