using One4All.Core.Common;
using One4All.Core.System;

namespace One4All.Application.Abstractions.Scheduling;

/// <summary>A task a scheduled job can run. Register implementations; each is found by its key.</summary>
public interface IScheduledTask
{
    /// <summary>The key jobs refer to it by.</summary>
    string Key { get; }

    /// <summary>Runs the task for a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runAt">The moment it runs for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What it did, or why it failed.</returns>
    Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default);
}

/// <summary>A job's schedule.</summary>
public sealed class JobDefinition
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The job code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The registered task it runs.</summary>
    public string TaskKey { get; init; } = string.Empty;

    /// <summary>How often.</summary>
    public JobFrequency Frequency { get; init; }

    /// <summary>At what time (UTC).</summary>
    public TimeOnly TimeOfDay { get; init; }

    /// <summary>On which day (weekly).</summary>
    public DayOfWeek? DayOfWeek { get; init; }

    /// <summary>On which day (monthly).</summary>
    public int? DayOfMonth { get; init; }

    /// <summary>The moment the schedule starts from.</summary>
    public DateTimeOffset From { get; init; }
}

/// <summary>A run in a job's history.</summary>
/// <param name="StartedAt">When it started.</param>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Message">What it did, or why it failed.</param>
public sealed record JobRunEntry(DateTimeOffset StartedAt, bool Succeeded, string Message);

/// <summary>What a pass of the scheduler did.</summary>
/// <param name="Ran">Jobs run.</param>
/// <param name="Failed">Of which failed.</param>
public sealed record SchedulerPass(int Ran, int Failed);

/// <summary>
/// Scheduled jobs (brief §4.17): registered tasks run per company on an hourly, daily, weekly or monthly schedule; every
/// run kept with its outcome; a failing task never stops the others; and a job can be run on demand.
/// </summary>
public interface IJobScheduler
{
    /// <summary>The registered task keys.</summary>
    IReadOnlyList<string> TaskKeys { get; }

    /// <summary>Defines a job.</summary>
    /// <param name="definition">The job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>When it first runs, or why it was refused.</returns>
    Task<Result<DateTimeOffset>> DefineAsync(JobDefinition definition, CancellationToken ct = default);

    /// <summary>Enables or disables a job.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The job.</param>
    /// <param name="enabled">Whether it runs.</param>
    /// <param name="now">The moment (the next run is scheduled from it).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetEnabledAsync(long companyId, string code, bool enabled, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Runs every enabled job that is due — across all companies, or one — and schedules each one's next run.</summary>
    /// <param name="now">The moment.</param>
    /// <param name="companyId">Only this company's jobs, when given.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What ran.</returns>
    Task<SchedulerPass> RunDueAsync(DateTimeOffset now, long? companyId = null, CancellationToken ct = default);

    /// <summary>Runs a job now, outside its schedule (its next scheduled run is unchanged).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The job.</param>
    /// <param name="now">The moment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run, or why it could not start.</returns>
    Task<Result<JobRunEntry>> RunNowAsync(long companyId, string code, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>A job's runs, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The runs, or why there are none.</returns>
    Task<Result<IReadOnlyList<JobRunEntry>>> HistoryAsync(long companyId, string code, CancellationToken ct = default);
}
