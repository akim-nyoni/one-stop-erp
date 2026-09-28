using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the finance-charge interest arithmetic (simple interest, actual/365).</summary>
public sealed class FinanceChargeTests
{
    [Fact]
    public void Interest_is_simple_interest_actual_over_365()
    {
        // 1000 @ 12% p.a. for 90 days = 1000 * 0.12 * 90/365 = 29.5890… → 29.59
        FinanceCharge.Interest(1000m, 90, 12m).Should().Be(29.59m);
    }

    [Fact]
    public void A_full_year_at_the_rate_is_the_whole_percentage()
    {
        FinanceCharge.Interest(1000m, 365, 10m).Should().Be(100.00m);
    }

    [Theory]
    [InlineData(0, 90, 12)]     // nothing outstanding
    [InlineData(1000, 0, 12)]   // not overdue
    [InlineData(1000, -5, 12)]  // due in the future
    [InlineData(1000, 90, 0)]   // no rate
    [InlineData(1000, 90, -3)]  // negative rate
    public void Nothing_is_charged_when_a_factor_is_non_positive(double amount, int days, double rate)
    {
        FinanceCharge.Interest((decimal)amount, days, (decimal)rate).Should().Be(0m);
    }
}
