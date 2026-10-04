using FluentAssertions;
using One4All.Application.Scheduling;
using One4All.Core.System;
using Xunit;

namespace One4All.UnitTests.Scheduling;

public class JobScheduleTests
{
    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Hourly_jobs_run_at_their_minute()
    {
        JobSchedule.NextRun(JobFrequency.Hourly, new TimeOnly(0, 15), null, null, At(5, 1, 10, 0)).Should().Be(At(5, 1, 10, 15));
        JobSchedule.NextRun(JobFrequency.Hourly, new TimeOnly(0, 15), null, null, At(5, 1, 10, 15)).Should().Be(At(5, 1, 11, 15));
    }

    [Fact]
    public void Daily_jobs_run_later_today_or_tomorrow()
    {
        JobSchedule.NextRun(JobFrequency.Daily, new TimeOnly(2, 0), null, null, At(5, 1, 1, 59)).Should().Be(At(5, 1, 2));
        JobSchedule.NextRun(JobFrequency.Daily, new TimeOnly(2, 0), null, null, At(5, 1, 10)).Should().Be(At(5, 2, 2));
    }

    [Fact]
    public void Weekly_jobs_run_on_their_day()
    {
        // 1 May 2026 is a Friday.
        JobSchedule.NextRun(JobFrequency.Weekly, new TimeOnly(7, 0), DayOfWeek.Monday, null, At(5, 1, 10)).Should().Be(At(5, 4, 7));
        JobSchedule.NextRun(JobFrequency.Weekly, new TimeOnly(7, 0), DayOfWeek.Friday, null, At(5, 1, 10)).Should().Be(At(5, 8, 7));
        JobSchedule.NextRun(JobFrequency.Weekly, new TimeOnly(11, 0), DayOfWeek.Friday, null, At(5, 1, 10)).Should().Be(At(5, 1, 11));
    }

    [Fact]
    public void Monthly_jobs_on_a_late_day_run_on_short_months_last_day()
    {
        JobSchedule.NextRun(JobFrequency.Monthly, new TimeOnly(6, 0), null, 31, At(2, 10, 0)).Should().Be(At(2, 28, 6));
        JobSchedule.NextRun(JobFrequency.Monthly, new TimeOnly(6, 0), null, 31, At(2, 28, 6)).Should().Be(At(3, 31, 6));
        JobSchedule.NextRun(JobFrequency.Monthly, new TimeOnly(6, 0), null, 31, At(4, 30, 7)).Should().Be(At(5, 31, 6));
    }

    [Fact]
    public void Weekly_and_monthly_jobs_need_their_day()
    {
        FluentActions.Invoking(() => JobSchedule.NextRun(JobFrequency.Weekly, default, null, null, At(5, 1, 0))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => JobSchedule.NextRun(JobFrequency.Monthly, default, null, 0, At(5, 1, 0))).Should().Throw<ArgumentException>();
    }
}
