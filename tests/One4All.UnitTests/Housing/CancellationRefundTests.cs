using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class CancellationRefundTests
{
    [Fact]
    public void Paid_less_rental_admin_and_arrears_is_refunded()
    {
        // Signed 10 Jan, cancelled 15 Apr: three months and a part month = 4 months at 100.
        CancellationRefund.Compute(3000m, new DateOnly(2026, 1, 10), new DateOnly(2026, 4, 15), 100m, 13000m, 5m, 200m)
            .Should().Be(new RefundComputation(4, 400m, 650m, 1750m));
    }

    [Fact]
    public void Whole_months_are_not_rounded_up_and_the_refund_is_never_negative()
    {
        CancellationRefund.Compute(500m, new DateOnly(2026, 1, 10), new DateOnly(2026, 3, 10), 100m, 13000m, 5m, 0m)
            .Should().Be(new RefundComputation(2, 200m, 650m, 0m));
    }
}
