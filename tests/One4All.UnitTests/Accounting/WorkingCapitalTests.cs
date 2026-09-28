using FluentAssertions;
using One4All.Application.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers the days-outstanding (DSO/DPO) arithmetic.</summary>
public sealed class WorkingCapitalTests
{
    [Fact]
    public void Days_outstanding_is_balance_over_flow_times_days()
    {
        // 1000 receivables against 1000 sales over 31 days = 31 days.
        WorkingCapital.DaysOutstanding(1000m, 1000m, 31).Should().Be(31.0m);
        // 500 against 1000 over 30 days = 15 days.
        WorkingCapital.DaysOutstanding(500m, 1000m, 30).Should().Be(15.0m);
    }

    [Theory]
    [InlineData(100, 0, 30)]  // no flow
    [InlineData(100, 1000, 0)] // no period
    public void Zero_flow_or_period_yields_zero(double balance, double flow, int days)
    {
        WorkingCapital.DaysOutstanding((decimal)balance, (decimal)flow, days).Should().Be(0m);
    }
}
