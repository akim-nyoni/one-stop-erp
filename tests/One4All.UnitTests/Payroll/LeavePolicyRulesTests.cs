using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers leave policy arithmetic: service bands, monthly accrual with pro-rata joiners and leavers, and yearly grants.</summary>
public sealed class LeavePolicyRulesTests
{
    private static LeavePolicy Monthly()
    {
        LeavePolicy policy = new(1, "annual", 18m);
        policy.SetBands([(5, 24m), (10, 30m)]);
        return policy;
    }

    [Theory]
    [InlineData("2022-03-01", "2026-02-28", 18)]
    [InlineData("2021-03-01", "2026-03-01", 24)]
    [InlineData("2016-01-15", "2026-01-31", 30)]
    public void Service_bands_raise_the_entitlement(string engaged, string asAt, decimal expected)
        => LeavePolicyRules.AnnualEntitlement(Monthly(), Day(engaged), Day(asAt)).Should().Be(expected);

    [Fact]
    public void Monthly_accrual_is_a_twelfth_pro_rata_in_the_first_and_last_months()
    {
        LeavePolicy policy = Monthly();
        LeavePolicyRules.DaysForMonth(policy, Day("2024-01-01"), null, Day("2026-04-01")).Should().Be(1.5m);
        LeavePolicyRules.DaysForMonth(policy, Day("2026-04-16"), null, Day("2026-04-01")).Should().Be(0.75m); // 15 of 30 days
        LeavePolicyRules.DaysForMonth(policy, Day("2024-01-01"), Day("2026-04-15"), Day("2026-04-01")).Should().Be(0.75m);
        LeavePolicyRules.DaysForMonth(policy, Day("2026-05-02"), null, Day("2026-04-01")).Should().Be(0m);
        LeavePolicyRules.DaysForMonth(policy, Day("2024-01-01"), Day("2026-03-31"), Day("2026-04-01")).Should().Be(0m);
    }

    [Fact]
    public void A_yearly_grant_comes_at_the_year_start_and_joiners_get_the_months_left()
    {
        LeavePolicy policy = new(1, "SICK", 12m) { AccruesMonthly = false, LeaveYearStartMonth = 7 };
        LeavePolicyRules.DaysForMonth(policy, Day("2020-01-01"), null, Day("2026-07-01")).Should().Be(12m);
        LeavePolicyRules.DaysForMonth(policy, Day("2020-01-01"), null, Day("2026-08-01")).Should().Be(0m);
        LeavePolicyRules.DaysForMonth(policy, Day("2026-10-05"), null, Day("2026-10-01")).Should().Be(9m); // October → June: 9 months
    }

    private static DateOnly Day(string text) => DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
