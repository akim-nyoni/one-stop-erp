using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers the bonus and gratuity earned to a date, which the payroll provisions are trued up to.</summary>
public sealed class PayrollProvisionsTests
{
    [Fact]
    public void A_13th_cheque_is_earned_through_the_bonus_year_and_gratuity_for_each_year_of_service()
    {
        DateOnly yearStart = new(2026, 1, 1);
        PayrollProvisions.BonusEarned(3_650m, new DateOnly(2020, 1, 1), yearStart, new DateOnly(2026, 3, 31), 1m).Should().Be(900m, "90 of 365 days of a month's pay");
        PayrollProvisions.BonusEarned(3_650m, new DateOnly(2026, 3, 2), yearStart, new DateOnly(2026, 3, 31), 1m).Should().Be(300m, "joined on 2 March: 30 days");
        PayrollProvisions.BonusEarned(3_650m, new DateOnly(2026, 4, 1), yearStart, new DateOnly(2026, 3, 31), 1m).Should().Be(0m);
        PayrollProvisions.BonusEarned(3_650m, new DateOnly(2020, 1, 1), yearStart, new DateOnly(2027, 6, 30), 1m).Should().Be(3_650m, "capped at the year");

        PayrollProvisions.GratuityEarned(2_600m, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 30), 15m, 26m).Should().Be(1_500m, "15 days at 100 a day for 365 days");
        PayrollProvisions.GratuityEarned(2_600m, new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31), 15m, 26m).Should().Be(0m);
    }
}
