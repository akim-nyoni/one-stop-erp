using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers scheduled jobs: only registered tasks and complete schedules are accepted; due jobs run and are rescheduled; a
/// task that throws is recorded as failed, its half-done work discarded, and the other jobs still run; a disabled job does
/// not run; a job can be run on demand; and the built-in despatch task runs through the scheduler.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SchedulerTests
{
    private readonly AccountingFixture _fixture;

    public SchedulerTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Due_jobs_run_on_schedule_and_failures_are_contained()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        CountingTask counter = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(services =>
        {
            services.AddSingleton<IScheduledTask>(counter);
            services.AddScoped<IScheduledTask, ExplodingTask>();
        });

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IJobScheduler jobs = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        jobs.TaskKeys.Should().Contain(["messages.despatch", "housing.lapse-offers", "test.count", "test.explode"]);

        DateTimeOffset start = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        JobDefinition Job(string code, string task, JobFrequency frequency, TimeOnly time, DayOfWeek? weekday = null, int? monthDay = null, DateTimeOffset? from = null) => new()
        {
            CompanyId = companyId, Code = code, TaskKey = task, Frequency = frequency, TimeOfDay = time, DayOfWeek = weekday, DayOfMonth = monthDay, From = from ?? start,
        };
        (await jobs.DefineAsync(Job("NOPE", "test.nothing", JobFrequency.Daily, new TimeOnly(1, 0)))).Error.Code.Should().Be("Job.UnknownTask");
        (await jobs.DefineAsync(Job("WEEK", "test.count", JobFrequency.Weekly, new TimeOnly(1, 0)))).Error.Code.Should().Be("Job.Definition");
        (await jobs.DefineAsync(Job("count", "test.count", JobFrequency.Daily, new TimeOnly(2, 0)))).Value.Should().Be(new DateTimeOffset(2026, 5, 2, 2, 0, 0, TimeSpan.Zero));
        (await jobs.DefineAsync(Job("BOOM", "test.explode", JobFrequency.Hourly, new TimeOnly(0, 15)))).Value.Should().Be(start.AddMinutes(15));
        (await jobs.DefineAsync(Job("DESPATCH", "messages.despatch", JobFrequency.Monthly, new TimeOnly(6, 0), monthDay: 31, from: new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero))))
            .Value.Should().Be(new DateTimeOffset(2026, 2, 28, 6, 0, 0, TimeSpan.Zero));
        (await jobs.DefineAsync(Job("COUNT", "test.count", JobFrequency.Daily, new TimeOnly(3, 0)))).Error.Code.Should().Be("Job.Exists");

        // 10:20: the overdue despatch and the hourly job run; the hourly one throws, but the despatch still runs.
        SchedulerPass first = await jobs.RunDueAsync(start.AddMinutes(20));
        (first.Ran, first.Failed).Should().Be((2, 1));
        counter.Runs.Should().Be(0);
        IReadOnlyList<JobRunEntry> boom = (await jobs.HistoryAsync(companyId, "BOOM")).Value;
        boom.Should().ContainSingle().Which.Message.Should().Be("InvalidOperationException: Kaboom");
        (await db.Customers.AnyAsync(c => c.CompanyId == companyId && c.Code == "HALF-DONE")).Should().BeFalse();
        (await jobs.HistoryAsync(companyId, "DESPATCH")).Value.Single().Should().Be(new JobRunEntry(start.AddMinutes(20), true, "0 sent, 0 to retry, 0 failed."));
        ScheduledJob despatch = await db.Set<ScheduledJob>().AsNoTracking().SingleAsync(j => j.CompanyId == companyId && j.Code == "DESPATCH");
        despatch.NextRunAt.Should().Be(new DateTimeOffset(2026, 5, 31, 6, 0, 0, TimeSpan.Zero));

        // 2 May 02:00: the daily job's first run, and the hourly job again.
        SchedulerPass second = await jobs.RunDueAsync(new DateTimeOffset(2026, 5, 2, 2, 0, 0, TimeSpan.Zero));
        (second.Ran, second.Failed).Should().Be((2, 1));
        counter.Runs.Should().Be(1);

        // Disabled, the daily job is skipped; it can still be run on demand without moving its schedule.
        (await jobs.SetEnabledAsync(companyId, "COUNT", false, new DateTimeOffset(2026, 5, 2, 3, 0, 0, TimeSpan.Zero))).IsSuccess.Should().BeTrue();
        (await jobs.SetEnabledAsync(companyId, "BOOM", false, new DateTimeOffset(2026, 5, 2, 3, 0, 0, TimeSpan.Zero))).IsSuccess.Should().BeTrue();
        (await jobs.RunDueAsync(new DateTimeOffset(2026, 5, 3, 3, 0, 0, TimeSpan.Zero))).Ran.Should().Be(0);
        (await jobs.RunNowAsync(companyId, "count", new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero))).Value.Succeeded.Should().BeTrue();
        counter.Runs.Should().Be(2);
        (await jobs.HistoryAsync(companyId, "COUNT")).Value.Select(r => r.StartedAt.Day).Should().Equal(3, 2);
    }

    private sealed class CountingTask : IScheduledTask
    {
        public int Runs { get; private set; }

        public string Key => "test.count";

        public Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
        {
            Runs++;
            return Task.FromResult(Result<string>.Success($"Run {Runs}"));
        }
    }

    private sealed class ExplodingTask : IScheduledTask
    {
        private readonly One4AllDbContext _db;

        public ExplodingTask(One4AllDbContext db) => _db = db;

        public string Key => "test.explode";

        public Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
        {
            _db.Customers.Add(new Customer(companyId, "HALF-DONE", "Should never be saved", Scenario.Usd));
            throw new InvalidOperationException("Kaboom");
        }
    }
}
