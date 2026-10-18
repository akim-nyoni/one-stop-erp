using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class WaitingListRenewalTests
{
    private static readonly DateOnly Confirmed = new(2025, 1, 15);

    [Theory]
    [InlineData(2026, 1, 15, ApplicantStatus.Active, RenewalAction.Remind)]    // due today: not yet overdue, so reminded (not yet reminded this cycle)
    [InlineData(2026, 1, 16, ApplicantStatus.Active, RenewalAction.Suspend)]   // overdue
    [InlineData(2026, 2, 14, ApplicantStatus.Suspended, RenewalAction.None)]   // within 30 days' grace
    [InlineData(2026, 2, 15, ApplicantStatus.Suspended, RenewalAction.Remove)] // grace over
    [InlineData(2026, 3, 1, ApplicantStatus.Active, RenewalAction.Suspend)]    // never suspended: suspended (and told) first, however late
    [InlineData(2026, 3, 1, ApplicantStatus.Removed, RenewalAction.None)]
    public void Overdue_applicants_are_suspended_then_removed(int y, int m, int d, ApplicantStatus status, RenewalAction expected)
    {
        WaitingListRenewal.Assess(status, Confirmed, new DateOnly(y, m, d), 12, 30).Should().Be(expected);
    }

    [Fact]
    public void A_reminder_goes_out_once_before_the_renewal_falls_due()
    {
        WaitingListRenewal.Assess(ApplicantStatus.Active, Confirmed, new DateOnly(2025, 12, 15), 12, 30).Should().Be(RenewalAction.None, "more than 30 days before");
        WaitingListRenewal.Assess(ApplicantStatus.Active, Confirmed, new DateOnly(2025, 12, 16), 12, 30).Should().Be(RenewalAction.Remind);
        WaitingListRenewal.Assess(ApplicantStatus.Active, Confirmed, new DateOnly(2026, 1, 10), 12, 30, remindedThisCycle: true).Should().Be(RenewalAction.None);
    }

    [Fact]
    public void Removal_waits_for_the_grace_period_after_the_suspension_notice()
    {
        DateOnly suspended = new(2026, 3, 1);
        WaitingListRenewal.Assess(ApplicantStatus.Suspended, Confirmed, new DateOnly(2026, 3, 30), 12, 30, suspended).Should().Be(RenewalAction.None);
        WaitingListRenewal.Assess(ApplicantStatus.Suspended, Confirmed, new DateOnly(2026, 3, 31), 12, 30, suspended).Should().Be(RenewalAction.Remove);
    }
}
