using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers grade entitlements (a listed grade replaces the policy's days, a higher service band still wins) and time off in lieu.</summary>
public sealed class LeaveGradeRulesTests
{
    [Fact]
    public void Grades_set_the_entitlement_and_overtime_earns_time_off()
    {
        LeavePolicy policy = new(1, "ANNUAL", 20m) { GradeDays = "B1=24; M1=30" };
        policy.SetBands([(10, 28m)]);
        DateOnly asAt = new(2026, 3, 31);

        LeavePolicyRules.AnnualEntitlement(policy, asAt.AddYears(-2), asAt).Should().Be(20m);
        LeavePolicyRules.AnnualEntitlement(policy, asAt.AddYears(-2), asAt, "b1").Should().Be(24m);
        LeavePolicyRules.AnnualEntitlement(policy, asAt.AddYears(-12), asAt, "B1").Should().Be(28m, "ten years' service gives more than the grade");
        LeavePolicyRules.AnnualEntitlement(policy, asAt.AddYears(-12), asAt, "M1").Should().Be(30m, "the grade gives more than the band");
        LeavePolicyRules.GradeDays("B1=24", "C3").Should().BeNull();

        LeavePolicyRules.TimeOffInLieu(12m, 8m).Should().Be(1.5m);
        LeavePolicyRules.TimeOffInLieu(8m, 8m, 1.5m).Should().Be(1.5m);
        LeavePolicyRules.TimeOffInLieu(0m, 8m).Should().Be(0m);
    }
}
