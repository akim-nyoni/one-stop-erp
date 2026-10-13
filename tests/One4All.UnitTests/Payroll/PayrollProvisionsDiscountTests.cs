using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers discounting gratuity to present value and earning long-service awards towards the next milestone.</summary>
public sealed class PayrollProvisionsDiscountTests
{
    [Fact]
    public void Gratuity_is_discounted_to_retirement_and_long_service_accrues_to_the_next_milestone()
    {
        DateOnly asAt = new(2026, 3, 31);

        // 10 years to retirement at 10% discount and 5% growth: 1000 × (1.05/1.10)^10 ≈ 628.
        PayrollProvisions.PresentValue(1_000m, asAt.AddYears(-55), asAt, 65, 10m, 5m).Should().BeApproximately(628.0m, 1m);
        PayrollProvisions.PresentValue(1_000m, null, asAt, 65, 10m, 5m).Should().Be(1_000m, "no date of birth, no discounting");
        PayrollProvisions.PresentValue(1_000m, asAt.AddYears(-55), asAt, 65, 0m, 5m).Should().Be(1_000m);
        PayrollProvisions.PresentValue(1_000m, asAt.AddYears(-70), asAt, 65, 10m, 5m).Should().Be(1_000m, "past retirement nothing is discounted");

        (int, decimal)[] awards = [(10, 1m), (20, 2m)];
        PayrollProvisions.LongServiceEarned(1_200m, asAt.AddYears(-5), asAt, awards).Should().BeApproximately(600m, 1m, "half way to 10 years: half a month");
        PayrollProvisions.LongServiceEarned(1_200m, asAt.AddYears(-15), asAt, awards).Should().BeApproximately(1_200m, 1m, "half way from 10 to 20 years: half of two months");
        PayrollProvisions.LongServiceEarned(1_200m, asAt.AddYears(-25), asAt, awards).Should().Be(0m);
    }
}
