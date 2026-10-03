using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class LicenceRenewalTests
{
    private static readonly DateOnly Expiry = new(2026, 12, 31);

    [Fact]
    public void An_early_renewal_runs_from_the_old_expiry_without_penalty()
    {
        LicenceRenewal.Renew(Expiry, new DateOnly(2026, 12, 15), 12, 200m, 10m)
            .Should().Be(new RenewalTerms(1, new DateOnly(2027, 12, 31), 200m, 0m, false));
    }

    [Fact]
    public void Renewing_on_the_expiry_date_is_not_late()
    {
        LicenceRenewal.Renew(Expiry, Expiry, 12, 200m, 10m).Late.Should().BeFalse();
    }

    [Fact]
    public void A_late_renewal_within_the_cycle_pays_a_penalty()
    {
        RenewalTerms terms = LicenceRenewal.Renew(Expiry, new DateOnly(2027, 2, 10), 12, 200m, 10m);
        terms.Should().Be(new RenewalTerms(1, new DateOnly(2027, 12, 31), 200m, 20m, true));
        terms.Total.Should().Be(220m);
    }

    [Fact]
    public void A_missed_cycle_is_charged_too()
    {
        LicenceRenewal.Renew(Expiry, new DateOnly(2028, 3, 1), 12, 200m, 10m)
            .Should().Be(new RenewalTerms(2, new DateOnly(2028, 12, 31), 400m, 40m, true));
    }

    [Theory]
    [InlineData(2026, 2, 28, 1, 2026, 3, 31)]
    [InlineData(2026, 1, 31, 1, 2026, 2, 28)]
    [InlineData(2026, 1, 15, 1, 2026, 2, 15)]
    [InlineData(2026, 3, 31, 3, 2026, 6, 30)]
    public void Month_end_expiries_stay_on_the_month_end(int y, int m, int d, int months, int ey, int em, int ed)
    {
        LicenceRenewal.Extend(new DateOnly(y, m, d), months).Should().Be(new DateOnly(ey, em, ed));
    }

    [Fact]
    public void A_cycle_must_be_at_least_a_month()
    {
        Action act = () => LicenceRenewal.Renew(Expiry, Expiry, 0, 200m, 10m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
