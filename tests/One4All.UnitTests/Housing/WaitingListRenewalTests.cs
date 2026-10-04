using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class WaitingListRenewalTests
{
    private static readonly DateOnly Confirmed = new(2025, 1, 15);

    [Theory]
    [InlineData(2026, 1, 15, ApplicantStatus.Active, RenewalAction.None)]      // due today: not yet overdue
    [InlineData(2026, 1, 16, ApplicantStatus.Active, RenewalAction.Suspend)]   // overdue
    [InlineData(2026, 2, 14, ApplicantStatus.Suspended, RenewalAction.None)]   // within 30 days' grace
    [InlineData(2026, 2, 15, ApplicantStatus.Suspended, RenewalAction.Remove)] // grace over
    [InlineData(2026, 3, 1, ApplicantStatus.Active, RenewalAction.Remove)]     // never suspended but long past grace
    [InlineData(2026, 3, 1, ApplicantStatus.Removed, RenewalAction.None)]
    public void Overdue_applicants_are_suspended_then_removed(int y, int m, int d, ApplicantStatus status, RenewalAction expected)
    {
        WaitingListRenewal.Assess(status, Confirmed, new DateOnly(y, m, d), 12, 30).Should().Be(expected);
    }
}
