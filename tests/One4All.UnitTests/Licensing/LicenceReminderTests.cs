using FluentAssertions;
using One4All.Application.Licensing;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>Covers the desktop's licence reminder: daily from seven days before expiry and while the licence is not valid, never twice in a day.</summary>
public sealed class LicenceReminderTests
{
    [Fact]
    public void The_reminder_shows_once_a_day_from_a_week_before_expiry()
    {
        DateOnly today = new(2026, 10, 9);
        LicenceReminder.ShouldRemind(LicenseStatus.Valid, 30, null, today).Should().BeFalse();
        LicenceReminder.ShouldRemind(LicenseStatus.Valid, null, null, today).Should().BeFalse("a perpetual licence needs no reminder");
        LicenceReminder.ShouldRemind(LicenseStatus.Valid, 7, null, today).Should().BeTrue();
        LicenceReminder.ShouldRemind(LicenseStatus.Valid, 7, today, today).Should().BeFalse("already shown today");
        LicenceReminder.ShouldRemind(LicenseStatus.Valid, 6, today.AddDays(-1), today).Should().BeTrue();
        LicenceReminder.ShouldRemind(LicenseStatus.Grace, -2, null, today).Should().BeTrue();
        LicenceReminder.ShouldRemind(LicenseStatus.ReadOnly, null, null, today).Should().BeTrue();
    }
}
