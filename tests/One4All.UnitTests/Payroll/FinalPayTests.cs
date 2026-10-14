using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers the final-pay rules: entitlements by termination type, the last month pro rata (nil once paid), and loans capped at what the pay can bear.</summary>
public sealed class FinalPayTests
{
    private static readonly TerminalPolicy Policy = new(1m, 22m, 2m, 4m, 10m);

    [Theory]
    [InlineData(TerminationType.Retrenchment, true, true, true)]
    [InlineData(TerminationType.Retirement, false, true, false)]
    [InlineData(TerminationType.Death, false, true, false)]
    [InlineData(TerminationType.EndOfContract, false, true, false)]
    [InlineData(TerminationType.Resignation, false, false, false)]
    [InlineData(TerminationType.Dismissal, false, false, false)]
    [InlineData(TerminationType.Absconding, false, false, false)]
    public void Types_carry_their_entitlements(TerminationType type, bool notice, bool gratuity, bool severance)
        => FinalPay.Entitlements(type).Should().Be((notice, gratuity, severance));

    [Fact]
    public void The_last_month_is_pro_rata_unless_paid()
    {
        FinalPay.ProRata(3_100m, new DateOnly(2026, 3, 10)).Should().Be(1_000m);
        FinalPay.ProRata(2_800m, new DateOnly(2026, 2, 28)).Should().Be(2_800m);
        FinalPay.Calculate(TerminationType.Resignation, 3_100m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 10), lastMonthPaid: true, 0m, 0m, Policy, _ => 0m).ProRataSalary.Should().Be(0m);
    }

    [Fact]
    public void Loans_are_recovered_as_far_as_the_pay_allows()
    {
        FinalPayStatement statement = FinalPay.Calculate(TerminationType.Resignation, 3_000m, new DateOnly(2024, 1, 1), new DateOnly(2026, 6, 15), false, 0m, 2_000m, Policy, ordinary => ordinary * 0.1m);
        (statement.ProRataSalary, statement.OrdinaryTax, statement.Recoveries, statement.UnrecoveredLoans, statement.Net).Should().Be((1_500m, 150m, 1_350m, 650m, 0m));
        statement.Net.Should().Be(statement.Gross - statement.OrdinaryTax - statement.TerminalTax - statement.Recoveries);
    }

    [Fact]
    public void The_bonus_is_paid_pro_rata_and_property_is_recovered_after_loans()
    {
        FinalPay.ProRataBonus(3_650m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31)).Should().Be(900m); // 90 of 365 days
        FinalPay.ProRataBonus(3_650m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).Should().Be(310m); // engaged this year: 31 days
        FinalPayStatement statement = FinalPay.Calculate(TerminationType.Resignation, 3_100m, new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31), false, 0m, 1_000m, Policy, _ => 0m,
            annualBonus: 3_650m, otherDeductions: 3_500m);
        (statement.ProRataBonus, statement.Recoveries, statement.OtherRecoveries, statement.UnrecoveredOther, statement.Net).Should().Be((900m, 1_000m, 3_000m, 500m, 0m));
    }
}
