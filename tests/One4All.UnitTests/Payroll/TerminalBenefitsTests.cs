using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

public sealed class TerminalBenefitsTests
{
    [Fact]
    public void Completed_years_count_whole_anniversaries()
    {
        TerminalBenefits.CompletedYears(new DateOnly(2020, 3, 15), new DateOnly(2026, 3, 14)).Should().Be(5);
        TerminalBenefits.CompletedYears(new DateOnly(2020, 3, 15), new DateOnly(2026, 3, 15)).Should().Be(6);
        TerminalBenefits.CompletedYears(new DateOnly(2026, 3, 15), new DateOnly(2025, 1, 1)).Should().Be(0);
    }

    [Fact]
    public void A_retrenchment_settles_notice_leave_gratuity_and_severance_less_recoveries_and_tax()
        => TerminalBenefits.Settle(2_600m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31), 10m, retrenched: true, 500m, new TerminalPolicy(1m, 21.67m, 2m, 4m, 10m))
            .Should().Be(new FinalSettlement(6, 2_600m, 1_199.82m, 7_200m, 14_400m, 500m, 2_160m, 22_739.82m));

    [Fact]
    public void A_resignation_earns_no_severance()
        => TerminalBenefits.Settle(2_600m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31), 0m, retrenched: false, 0m, new TerminalPolicy(1m, 21.67m, 2m, 4m, 0m)).Severance.Should().Be(0m);

    [Fact]
    public void The_provision_is_discounted_from_retirement()
    {
        TerminalBenefits.Provision(2_600m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31), new DateOnly(1966, 4, 1), 2m, 65, 10m).Should().Be((7_200m, 6, 4_064.21m));
        TerminalBenefits.Provision(2_600m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31), null, 2m, 65, 10m).Should().Be((7_200m, 0, 7_200m));
    }

    [Fact]
    public void Terminal_tax_takes_the_tax_free_part_off_then_the_rate_or_the_table()
    {
        TerminalPolicy policy = new(1m, 22m, 2m, 4m, 10m, TaxFreeAmount: 2_000m);
        TerminalBenefits.TerminalTax(5_000m, policy).Should().Be(300m, "10% of the 3,000 above the tax-free 2,000");
        TerminalBenefits.TerminalTax(1_500m, policy).Should().Be(0m);
        TerminalBenefits.TerminalTax(5_000m, policy, taxable => taxable > 1_000m ? (taxable - 1_000m) * 0.25m : 0m).Should().Be(500m, "the table's bands on the taxable 3,000");
        TerminalBenefits.TerminalTax(5_000m, new TerminalPolicy(1m, 22m, 2m, 4m, 10m)).Should().Be(500m, "no tax-free part: the flat rate on all of it");
    }
}
